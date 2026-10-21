#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)][Alias('Contexte')][ValidateNotNullOrEmpty()][string] $Context,
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9](?:[A-Za-z0-9-]{0,37}[A-Za-z0-9])?$')][string] $GitHubUser,
    [Parameter(Mandatory)][ValidatePattern('^sha256:[a-f0-9]{64}$')][string] $DedicatedImageDigest,
    [string] $KubeConfig,
    [string] $TokenFilePath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.Net.Http

function ConvertTo-WindowsProcessArgument([string] $Value) {
    $escaped = $Value -replace '(\\*)"', '$1$1\"'
    $escaped = $escaped -replace '(\\+)$', '$1$1'
    return '"' + $escaped + '"'
}

function Assert-ReadOnlyPackageScopes([string] $Scopes) {
    $values = @($Scopes.Split(',') | ForEach-Object { $_.Trim().ToLowerInvariant() } |
        Where-Object { $_.Length -gt 0 } | Select-Object -Unique)
    if ($values.Count -ne 1 -or $values[0] -ne 'read:packages') {
        throw 'Le PAT classique doit avoir uniquement read:packages. Toute autre portée ou portée non vérifiable est refusée.'
    }
}

function Invoke-PrivateKubectl([string[]] $Arguments, [string] $InputText = $null) {
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $kubectlPath
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.RedirectStandardInput = $true
    if ($start.PSObject.Properties.Name -contains 'StandardInputEncoding') {
        $start.StandardInputEncoding = [Text.UTF8Encoding]::new($false)
    }
    $allArguments = $kubectlArguments + $Arguments
    if ($start.PSObject.Properties.Name -contains 'ArgumentList') {
        foreach ($argument in $allArguments) { $start.ArgumentList.Add($argument) }
    }
    else {
        $start.Arguments = ($allArguments | ForEach-Object { ConvertTo-WindowsProcessArgument $_ }) -join ' '
    }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    try {
        $previousInputEncoding = $null
        try {
            if ($start.PSObject.Properties.Name -notcontains 'StandardInputEncoding') {
                # .NET Framework takes the child stdin encoding from the console.
                $previousInputEncoding = [Console]::InputEncoding
                [Console]::InputEncoding = [Text.UTF8Encoding]::new($false)
            }
            [void]$process.Start()
        }
        finally {
            if ($previousInputEncoding) { [Console]::InputEncoding = $previousInputEncoding }
        }
        $output = $process.StandardOutput.ReadToEndAsync()
        $errors = $process.StandardError.ReadToEndAsync()
        if ($InputText) {
            $inputBytes = [Text.Encoding]::UTF8.GetBytes($InputText)
            try { $process.StandardInput.BaseStream.Write($inputBytes, 0, $inputBytes.Length) }
            finally { [Array]::Clear($inputBytes, 0, $inputBytes.Length) }
        }
        $process.StandardInput.Close()
        if (!$process.WaitForExit(30000)) { $process.Kill(); throw 'kubectl timeout' }
        [void]$errors.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) { throw 'kubectl failure' }
        return $output.GetAwaiter().GetResult().Trim()
    }
    catch { throw "La vérification ou création Kubernetes a échoué. Aucun contenu de Secret ni erreur brute n'est affiché." }
    finally { $process.Dispose() }
}

function Read-RegistryToken {
    if ($TokenFilePath) {
        $buffer = [byte[]]::new(4097)
        $stream = $null
        try {
            $stream = [IO.File]::OpenRead($TokenFilePath)
            $count = 0
            while ($count -lt $buffer.Length) {
                $read = $stream.Read($buffer, $count, $buffer.Length - $count)
                if ($read -eq 0) { break }
                $count += $read
            }
            if ($count -gt 4096) { throw 'oversized token file' }
            $encoding = [Text.UTF8Encoding]::new($false, $true)
            $offset = 0
            if ($count -ge 3 -and $buffer[0] -eq 0xef -and $buffer[1] -eq 0xbb -and $buffer[2] -eq 0xbf) {
                $offset = 3
            }
            elseif ($count -ge 2 -and $buffer[0] -eq 0xff -and $buffer[1] -eq 0xfe) {
                $encoding = [Text.UnicodeEncoding]::new($false, $false, $true)
                $offset = 2
            }
            elseif ($count -ge 2 -and $buffer[0] -eq 0xfe -and $buffer[1] -eq 0xff) {
                $encoding = [Text.UnicodeEncoding]::new($true, $false, $true)
                $offset = 2
            }
            return $encoding.GetString($buffer, $offset, ($count - $offset)).Trim()
        }
        catch { throw 'Le fichier privé du PAT est illisible ou invalide ; son chemin et son contenu restent masqués.' }
        finally {
            if ($stream) { $stream.Dispose() }
            [Array]::Clear($buffer, 0, $buffer.Length)
        }
    }
    $secure = Read-Host -Prompt 'PAT classique GitHub avec uniquement read:packages (saisie masquée)' -AsSecureString
    $pointer = [IntPtr]::Zero
    try {
        $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer).Trim()
    }
    finally {
        if ($pointer -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
        $secure.Dispose()
    }
}

$namespace = 'arkya-beta'
$secretName = 'arkya-ghcr'
$repository = 'hantse/arkya-mmorpg-dedicated'
if ($Context -match '\p{C}') { throw 'Le nom de contexte est invalide.' }
$kubectlPath = (Get-Command kubectl -CommandType Application -ErrorAction Stop).Source
$kubectlArguments = @('--context', $Context, '--request-timeout=15s')
if ($KubeConfig) { $kubectlArguments += @('--kubeconfig', $KubeConfig) }

$configuration = Invoke-PrivateKubectl @('config', 'view', '--minify', '--output=json') | ConvertFrom-Json
if (@($configuration.contexts).Count -ne 1 -or $configuration.contexts[0].name -ne $Context -or @($configuration.clusters).Count -ne 1) {
    throw 'Choisissez explicitement un contexte Arkya existant.'
}
$cluster = $configuration.clusters[0].cluster
$endpoint = [Uri]$cluster.server
if ($endpoint.Scheme -ne 'https' -or $endpoint.UserInfo -or $endpoint.Query -or $endpoint.Fragment -or $endpoint.AbsolutePath -ne '/' -or
    ($cluster.PSObject.Properties.Name -contains 'insecure-skip-tls-verify' -and $cluster.'insecure-skip-tls-verify')) {
    throw 'Le contexte doit vérifier TLS vers une origine Kubernetes HTTPS explicite.'
}
Write-Output "Contexte : $Context ; API : $($endpoint.AbsoluteUri) ; namespace : $namespace."
foreach ($requiredNamespace in @($namespace, 'arkya-beta-realm')) {
    if ((Invoke-PrivateKubectl @('get', 'namespace', $requiredNamespace, '--output=name')) -ne "namespace/$requiredNamespace") {
        throw 'Un namespace Arkya requis est absent.'
    }
}
$deployment = Invoke-PrivateKubectl @('get', 'deployment', 'arkya-realms', '--namespace', $namespace,
    '--output=jsonpath={.metadata.name}{"|"}{.metadata.labels.app\.kubernetes\.io/name}{"|"}{.spec.template.spec.containers[*].image}')
$fields = $deployment.Split('|')
if ($fields.Count -ne 3 -or $fields[0] -ne 'arkya-realms' -or $fields[1] -ne 'arkya-realms' -or
    $fields[2] -notmatch '^ghcr\.io/hantse/arkya-services-realms@sha256:[a-f0-9]{64}$') {
    throw 'Le contexte ne contient pas le Deployment Arkya Realms attendu.'
}
$token = $null
$pullToken = $null
$encodedAuth = $null
$dockerJson = $null
$secretJson = $null
$authBytes = $null
$request = $null
$response = $null
$handler = [Net.Http.HttpClientHandler]::new()
$handler.AllowAutoRedirect = $false
$client = [Net.Http.HttpClient]::new($handler)
$client.Timeout = [TimeSpan]::FromSeconds(20)
$stage = 'lecture privée du PAT'
try {
    $token = Read-RegistryToken
    if ($token.Length -lt 32 -or $token.Length -gt 512 -or $token -match '\s|\p{C}') { throw 'invalid PAT' }
    $stage = 'validation de la portée GitHub'
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Head, 'https://api.github.com/user')
    try {
        $request.Headers.Authorization = [Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $token)
        $request.Headers.UserAgent.ParseAdd('Arkya-registry-provisioning')
        $request.Headers.Add('X-GitHub-Api-Version', '2022-11-28')
        $response = $client.SendAsync($request, [Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
        try {
            if (!$response.IsSuccessStatusCode) { throw 'GitHub authentication failed' }
            Assert-ReadOnlyPackageScopes ($response.Headers.GetValues('X-OAuth-Scopes') -join ',')
        }
        finally { $response.Dispose() }
    }
    finally { $request.Headers.Clear(); $request.Dispose(); $request = $null }
    $stage = "validation de la lecture de l'image dédiée"
    $authBytes = [Text.Encoding]::UTF8.GetBytes("${GitHubUser}:$token")
    $encodedAuth = [Convert]::ToBase64String($authBytes)
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Get,
        "https://ghcr.io/token?service=ghcr.io&scope=repository:${repository}:pull")
    try {
        $request.Headers.Authorization = [Net.Http.Headers.AuthenticationHeaderValue]::new('Basic', $encodedAuth)
        $response = $client.SendAsync($request, [Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
        try {
            if (!$response.IsSuccessStatusCode) { throw 'Registry authentication failed' }
            [void]$response.Content.LoadIntoBufferAsync(65536).GetAwaiter().GetResult()
            $pullToken = ($response.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json).token
            if ([string]::IsNullOrWhiteSpace($pullToken) -or $pullToken -match '\s|\p{C}') { throw 'Invalid registry response' }
        }
        finally { $response.Dispose() }
    }
    finally { $request.Headers.Clear(); $request.Dispose(); $request = $null }
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Head,
        "https://ghcr.io/v2/$repository/manifests/$DedicatedImageDigest")
    try {
        $request.Headers.Authorization = [Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $pullToken)
        $request.Headers.Accept.ParseAdd('application/vnd.oci.image.index.v1+json, application/vnd.docker.distribution.manifest.list.v2+json, application/vnd.oci.image.manifest.v1+json, application/vnd.docker.distribution.manifest.v2+json')
        $response = $client.SendAsync($request, [Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
        try {
            if (!$response.IsSuccessStatusCode -or ($response.Headers.GetValues('Docker-Content-Digest') -join '') -ne $DedicatedImageDigest) {
                throw 'The pinned dedicated image is not readable'
            }
        }
        finally { $response.Dispose() }
    }
    finally { $request.Headers.Clear(); $request.Dispose(); $request = $null }
    Write-Output 'GitHub : read:packages uniquement ; image dédiée : lecture validée.'
    $dockerJson = @{ auths = @{ 'ghcr.io' = @{ auth = $encodedAuth } } } | ConvertTo-Json -Depth 4 -Compress
    $secretJson = @{
        apiVersion = 'v1'; kind = 'Secret'; metadata = @{ name = $secretName; namespace = $namespace }
        type = 'kubernetes.io/dockerconfigjson'
        data = @{ '.dockerconfigjson' = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($dockerJson)) }
    } | ConvertTo-Json -Depth 5 -Compress
    $stage = 'création exclusive du Secret arkya-ghcr dans arkya-beta'
    $created = Invoke-PrivateKubectl @('create', '--namespace', $namespace, '--filename=-', '--output=name') $secretJson
    if ($created -ne 'secret/arkya-ghcr') { throw 'Unexpected Kubernetes create response' }
    Write-Output 'Secret arkya-ghcr créé dans arkya-beta.'
}
catch { throw "Échec : $stage. Aucune valeur privée ni erreur brute n’est affichée ; aucun Secret existant n’a été écrasé." }
finally {
    if ($authBytes) { [Array]::Clear($authBytes, 0, $authBytes.Length) }
    if ($request) { $request.Headers.Clear(); $request.Dispose() }
    $token = $pullToken = $encodedAuth = $dockerJson = $secretJson = $null
    $request = $response = $null
    $client.Dispose()
}
