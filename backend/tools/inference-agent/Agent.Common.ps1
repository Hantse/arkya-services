$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-AgentInstallRoot {
    param([string]$InstallDirectory)
    if ([string]::IsNullOrWhiteSpace($InstallDirectory)) {
        if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'config/agent.json')) { return $PSScriptRoot }
        $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
        if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
            return Join-Path $env:ProgramData 'ArkyaInference'
        }
        return Join-Path $env:LOCALAPPDATA 'ArkyaInference'
    }
    return [IO.Path]::GetFullPath($InstallDirectory)
}

function Write-AgentCredential {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][Security.SecureString]$Secret)
    Add-Type -AssemblyName System.Security
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Secret)
    $bytes = $null
    try {
        $bytes = [Text.Encoding]::UTF8.GetBytes([Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer))
        if ($bytes.Length -lt 32) { throw 'Agent token must contain at least 32 UTF-8 bytes.' }
        $protected = [Security.Cryptography.ProtectedData]::Protect($bytes,$null,[Security.Cryptography.DataProtectionScope]::LocalMachine)
        [IO.File]::WriteAllBytes($Path,$protected)
    } finally {
        if ($bytes) { [Array]::Clear($bytes,0,$bytes.Length) }
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
    }
}

function Read-AgentCredential {
    param([Parameter(Mandatory)][string]$Path)
    Add-Type -AssemblyName System.Security
    $bytes = [Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($Path),$null,[Security.Cryptography.DataProtectionScope]::LocalMachine)
    try { return [Text.Encoding]::UTF8.GetString($bytes) }
    finally { [Array]::Clear($bytes,0,$bytes.Length) }
}

function Assert-AgentEnrollmentIdentity {
    param([string]$AgentId, [string]$DisplayName)
    if (-not [regex]::IsMatch($AgentId,'\A[a-z][a-z0-9-]{1,46}[a-z0-9]\z')) {
        throw 'AgentId must contain 3-48 lowercase ASCII letters/digits/hyphens, start with a letter and end with a letter or digit.'
    }
    if ([string]::IsNullOrWhiteSpace($DisplayName) -or $DisplayName.Length -gt 80) {
        throw 'DisplayName must contain 1-80 characters and must not be blank.'
    }
    foreach ($character in $DisplayName.ToCharArray()) {
        if ([char]::IsControl($character)) { throw 'DisplayName must not contain control characters.' }
    }
}

function Get-AgentEnrollmentFailureMessage {
    param([int]$StatusCode, [string]$EnrollmentUrl, [string]$ConfigDirectory)
    $retry = "L'identité locale est conservée. Après correction côté serveur, relancez la même commande avec le même dossier d'installation."
    switch ($StatusCode) {
        404 {
            return "Route d'inscription introuvable (HTTP 404) sur $EnrollmentUrl. Vérifiez l'URL du backend, puis le déploiement et l'activation du service Arkya.Inference.Api et du routage /agent/inference/* dans la gateway. $retry"
        }
        409 {
            return "Conflit d'identité de l'agent (HTTP 409) sur $EnrollmentUrl. Dossier de configuration utilisé : $ConfigDirectory. Pour une mise à jour, reprenez le dossier de l'installation existante avec -InstallDirectory, le même compte Windows et le même mode d'élévation. Vérifiez C:\ProgramData\ArkyaInference et %LOCALAPPDATA%\ArkyaInference. L'identité locale est conservée."
        }
        { $_ -in @(502,503) } {
            return "Service d'inscription indisponible (HTTP $StatusCode) sur $EnrollmentUrl. Vérifiez que la gateway peut joindre le service Arkya.Inference.Api et que celui-ci est prêt. $retry"
        }
        default { return "Échec de l'inscription (HTTP $StatusCode) sur $EnrollmentUrl. $retry" }
    }
}

function Register-AgentEnrollment {
    param(
        [Parameter(Mandatory)][string]$BackendUrl,
        [Parameter(Mandatory)][string]$AgentId,
        [Parameter(Mandatory)][string]$DisplayName,
        [Parameter(Mandatory)][string]$ConfigDirectory,
        [Security.SecureString]$EnrollmentToken,
        [int]$InitialWorkers = 3, [int]$ThreadsPerWorker = 4, [int]$MaxWorkers = 6,
        [string]$ModelId = 'lfm', [switch]$AllowLoopbackHttp, [switch]$SystemOnly
    )
    Assert-AgentEnrollmentIdentity -AgentId $AgentId -DisplayName $DisplayName
    $backend = [Uri]$BackendUrl
    if (-not $backend.IsAbsoluteUri -or $backend.UserInfo -or $backend.Query -or $backend.Fragment -or
        ($backend.Scheme -ne 'https' -and -not ($AllowLoopbackHttp -and $backend.Scheme -eq 'http' -and $backend.IsLoopback))) {
        throw 'Enrollment requires HTTPS (explicit loopback HTTP tests only).'
    }
    $BackendUrl = $BackendUrl.TrimEnd('/')
    $enrollmentUrl = $BackendUrl + '/agent/inference/enroll'
    $statePath = Join-Path $ConfigDirectory 'enrollment-state.json'
    $tokenPath = Join-Path $ConfigDirectory 'agent-token.dpapi'
    if (Test-Path -LiteralPath $statePath) {
        $state = Get-Content -Raw -Encoding UTF8 -LiteralPath $statePath | ConvertFrom-Json
        if ($state.agentId -ne $AgentId -or $state.backendUrl -ne $BackendUrl) {
            throw 'Enrollment identity/backend differs from the durable local identity; use a separate installation directory.'
        }
        Assert-AgentEnrollmentIdentity -AgentId $state.agentId -DisplayName $state.displayName
        if (-not (Test-Path -LiteralPath $tokenPath)) { throw 'Enrollment credential is missing; refusing to replace the durable device identity.' }
        if ($state.completed) { return }
    } else {
        if (Test-Path -LiteralPath $tokenPath) { throw 'An agent credential exists without identity state. Use the matching registration or an explicit legacy AgentToken.' }
        $random = New-Object byte[] 32
        $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
        try { $generator.GetBytes($random) } finally { $generator.Dispose() }
        $deviceKey = [BitConverter]::ToString($random).Replace('-','').ToLowerInvariant()
        Write-AgentCredential -Path $tokenPath -Secret (ConvertTo-SecureString $deviceKey -AsPlainText -Force)
        $deviceKey = $null
        $state = [PSCustomObject]@{schemaVersion=1;backendUrl=$BackendUrl;agentId=$AgentId;displayName=$DisplayName;
            initialWorkers=$InitialWorkers;threadsPerWorker=$ThreadsPerWorker;maxWorkers=$MaxWorkers;modelId=$ModelId;completed=$false}
        [IO.File]::WriteAllText(($statePath + '.tmp'),($state | ConvertTo-Json),[Text.UTF8Encoding]::new($false))
        Move-Item -LiteralPath ($statePath + '.tmp') -Destination $statePath -Force
    }
    Add-Type -AssemblyName System.Net.Http
    if ($SystemOnly) {
        & icacls.exe $ConfigDirectory /setowner '*S-1-5-32-544' /T /Q | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Cannot secure durable enrollment identity.' }
        Assert-AgentSystemTrust $ConfigDirectory
    }
    $handler = New-Object Net.Http.HttpClientHandler
    $handler.AllowAutoRedirect = $false
    $client = New-Object Net.Http.HttpClient($handler)
    $client.Timeout = [TimeSpan]::FromSeconds(30)
    try {
        for ($attempt = 0; $attempt -lt 2; $attempt++) {
            $grantPointer = [IntPtr]::Zero
            $request = New-Object Net.Http.HttpRequestMessage([Net.Http.HttpMethod]::Post, $enrollmentUrl)
            try {
                if ($EnrollmentToken) {
                    $grantPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($EnrollmentToken)
                    $grant = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($grantPointer)
                    $request.Headers.Authorization = New-Object Net.Http.Headers.AuthenticationHeaderValue('Bearer',$grant)
                    $grant = $null
                }
                $body = @{agentId=$state.agentId;displayName=$state.displayName;agentToken=(Read-AgentCredential $tokenPath);
                    initialWorkers=$state.initialWorkers;threadsPerWorker=$state.threadsPerWorker;maxWorkers=$state.maxWorkers;modelId=$state.modelId} | ConvertTo-Json
                $request.Content = New-Object Net.Http.StringContent($body,[Text.Encoding]::UTF8,'application/json')
                $body = $null
                try { $response = $client.SendAsync($request).GetAwaiter().GetResult() }
                catch { throw "Connexion d'inscription impossible sur $enrollmentUrl. L'identité locale est conservée ; vérifiez la connexion et relancez la même commande avec le même dossier d'installation." }
                try {
                    if (-not $response.IsSuccessStatusCode) {
                        if ([int]$response.StatusCode -in @(401,403) -and -not $EnrollmentToken -and $attempt -eq 0) {
                            $EnrollmentToken = Read-Host "Jeton temporaire d'inscription fourni par le backoffice" -AsSecureString
                            continue
                        }
                        throw (Get-AgentEnrollmentFailureMessage -StatusCode ([int]$response.StatusCode) -EnrollmentUrl $enrollmentUrl -ConfigDirectory $ConfigDirectory)
                    }
                    $result = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
                    if ($result.agentId -ne $AgentId) { throw 'Enrollment response identity mismatch.' }
                    $state.completed = $true
                    [IO.File]::WriteAllText(($statePath + '.tmp'),($state | ConvertTo-Json),[Text.UTF8Encoding]::new($false))
                    Move-Item -LiteralPath ($statePath + '.tmp') -Destination $statePath -Force
                    return
                } finally { $response.Dispose() }
            } finally {
                if ($grantPointer -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($grantPointer) }
                $request.Dispose()
            }
        }
    } finally { $client.Dispose(); $handler.Dispose() }
}

function Set-AgentPrivateDirectory {
    param([Parameter(Mandatory)][string]$Path, [switch]$SystemOnly)
    $null = New-Item -ItemType Directory -Path $Path -Force
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent().User
    $directory = New-Object IO.DirectoryInfo($Path)
    $accessOnly = [Security.AccessControl.AccessControlSections]::Access
    $acl = if ($PSVersionTable.PSEdition -eq 'Core') {
        [IO.FileSystemAclExtensions]::GetAccessControl($directory, $accessOnly)
    } else { $directory.GetAccessControl($accessOnly) }
    $acl.SetAccessRuleProtection($true, $false)
    foreach ($rule in @($acl.GetAccessRules($true, $false, [Security.Principal.SecurityIdentifier]))) {
        $acl.RemoveAccessRuleSpecific($rule)
    }
    $inherit = [Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit'
    $propagate = [Security.AccessControl.PropagationFlags]::None
    $owner = if ($SystemOnly) { New-Object Security.Principal.SecurityIdentifier('S-1-5-32-544') } else { $identity }
    foreach ($sid in @($owner, (New-Object Security.Principal.SecurityIdentifier('S-1-5-18')))) {
        $rule = New-Object Security.AccessControl.FileSystemAccessRule($sid, 'FullControl', $inherit, $propagate, 'Allow')
        $acl.AddAccessRule($rule)
    }
    if ($PSVersionTable.PSEdition -eq 'Core') { [IO.FileSystemAclExtensions]::SetAccessControl($directory, $acl) }
    else { $directory.SetAccessControl($acl) }
}

function Get-OwnedAgentProcess {
    param([Parameter(Mandatory)][string]$InstallRoot)
    $identityPath = Join-Path $InstallRoot 'config/agent.pid.json'
    if (-not (Test-Path -LiteralPath $identityPath)) { return $null }
    $identity = Get-Content -Raw -Encoding UTF8 -LiteralPath $identityPath | ConvertFrom-Json
    $process = Get-Process -Id $identity.pid -ErrorAction SilentlyContinue
    if ($null -eq $process) { return $null }
    $expectedPython = [IO.Path]::GetFullPath((Join-Path $InstallRoot 'runtime/python/python.exe'))
    $expectedScript = [IO.Path]::GetFullPath((Join-Path $InstallRoot 'agent.py'))
    $expectedConfig = [IO.Path]::GetFullPath((Join-Path $InstallRoot 'config/agent.json'))
    $cim = Get-CimInstance Win32_Process -Filter "ProcessId=$($process.Id)"
    if ($process.Path -ne $expectedPython -or $identity.executable -ne $expectedPython -or
        $identity.script -ne $expectedScript -or $identity.config -ne $expectedConfig -or
        [long]$identity.creationFileTime -ne $process.StartTime.ToUniversalTime().ToFileTimeUtc() -or
        -not $cim.CommandLine.Contains($expectedScript) -or -not $cim.CommandLine.Contains($expectedConfig)) {
        throw 'PID ownership verification failed. No process was stopped or reused.'
    }
    return $process
}

function Stop-OwnedAgent {
    param([Parameter(Mandatory)][string]$InstallRoot)
    $process = Get-OwnedAgentProcess $InstallRoot
    $configurationDirectory = Join-Path $InstallRoot 'config'
    if (-not (Test-Path -LiteralPath $configurationDirectory)) { return }
    [IO.File]::WriteAllText((Join-Path $configurationDirectory 'stop.request'), 'stop')
    if ($null -eq $process) { return }
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        Start-Sleep -Milliseconds 500
        if ($process.HasExited) { return }
    }
    $process = Get-OwnedAgentProcess $InstallRoot
    if ($null -ne $process) {
        Stop-Process -Id $process.Id -ErrorAction Stop
        $process.WaitForExit(10000) | Out-Null
    }
}

function Watch-OwnedAgent {
    param(
        [Parameter(Mandatory)][scriptblock]$Launch,
        [Diagnostics.Process]$ExistingProcess,
        [Parameter(Mandatory)][string]$StopRequestPath,
        [string]$LogPath,
        [ValidateRange(0,60)][int]$RestartDelaySeconds = 10,
        [ValidateRange(0,3)][int]$MaxRestarts = 3
    )
    $process = $ExistingProcess
    $restarts = 0
    while ($true) {
        if (Test-Path -LiteralPath $StopRequestPath) { return 0 }
        if ($null -eq $process) { $process = & $Launch }
        if ($null -eq $process) { throw 'Agent launcher did not return a process.' }
        # Keep the real handle open so ExitCode remains available after an adopted PID exits.
        $null = $process.Handle
        $stopRequested = Test-Path -LiteralPath $StopRequestPath
        while (-not $process.WaitForExit(250)) {
            if (Test-Path -LiteralPath $StopRequestPath) { $stopRequested = $true }
        }
        $stopRequested = $stopRequested -or (Test-Path -LiteralPath $StopRequestPath)
        $exitCode = $process.ExitCode
        $process.Dispose()
        $process = $null
        if ($stopRequested -or $exitCode -eq 0) { return 0 }
        if ($restarts -ge $MaxRestarts) { return 1 }
        $restarts++
        if ($LogPath) {
            [IO.File]::AppendAllText($LogPath, ([DateTimeOffset]::UtcNow.ToString('O') +
                " restart $restarts/$MaxRestarts after agent exit $exitCode; delay ${RestartDelaySeconds}s`n"))
        }
        for ($tick = 0; $tick -lt ($RestartDelaySeconds * 4); $tick++) {
            if (Test-Path -LiteralPath $StopRequestPath) { return 0 }
            Start-Sleep -Milliseconds 250
        }
    }
}

function Assert-AgentNoReparsePoints {
    param([Parameter(Mandatory)][string]$Path)
    $queue = New-Object 'Collections.Generic.Queue[IO.FileSystemInfo]'
    $queue.Enqueue((Get-Item -LiteralPath $Path -Force))
    while ($queue.Count -gt 0) {
        $item = $queue.Dequeue()
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Reparse point refused: $($item.FullName)" }
        if ($item -is [IO.DirectoryInfo]) {
            foreach ($child in $item.GetFileSystemInfos()) { $queue.Enqueue($child) }
        }
    }
}

function Assert-AgentSystemTrust {
    param([Parameter(Mandatory)][string]$Path)
    $trusted = @('S-1-5-18','S-1-5-32-544')
    $writeRights = [Security.AccessControl.FileSystemRights]::WriteData -bor
        [Security.AccessControl.FileSystemRights]::AppendData -bor
        [Security.AccessControl.FileSystemRights]::WriteExtendedAttributes -bor
        [Security.AccessControl.FileSystemRights]::WriteAttributes -bor
        [Security.AccessControl.FileSystemRights]::Delete -bor
        [Security.AccessControl.FileSystemRights]::DeleteSubdirectoriesAndFiles -bor
        [Security.AccessControl.FileSystemRights]::ChangePermissions -bor
        [Security.AccessControl.FileSystemRights]::TakeOwnership
    $queue = New-Object 'Collections.Generic.Queue[IO.FileSystemInfo]'
    $queue.Enqueue((Get-Item -LiteralPath $Path -Force))
    while ($queue.Count -gt 0) {
        $item = $queue.Dequeue()
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'SYSTEM installation must not contain reparse points.' }
        $acl = Get-Acl -LiteralPath $item.FullName
        if ($acl.GetOwner([Security.Principal.SecurityIdentifier]).Value -notin $trusted) {
            throw 'Existing SYSTEM installation owner is not Administrators/SYSTEM. Refusing to trust its configuration or code.'
        }
        foreach ($rule in $acl.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier])) {
            if ($rule.AccessControlType -eq 'Allow' -and $rule.IdentityReference.Value -notin $trusted -and
                ($rule.FileSystemRights -band $writeRights)) {
                throw 'Existing SYSTEM installation grants write access outside Administrators/SYSTEM.'
            }
        }
        if ($item -is [IO.DirectoryInfo]) {
            foreach ($child in $item.GetFileSystemInfos()) { $queue.Enqueue($child) }
        }
    }
}

function Assert-AgentPackage {
    param([Parameter(Mandatory)][string]$PackageRoot)
    $manifestPath = Join-Path $PackageRoot 'package-manifest.json'
    if (-not (Test-Path -LiteralPath $manifestPath)) { throw 'Build-Package.ps1 must create the installable package first.' }
    $manifest = Get-Content -Raw -Encoding UTF8 -LiteralPath $manifestPath | ConvertFrom-Json
    $root = [IO.Path]::GetFullPath($PackageRoot).TrimEnd('\') + '\'
    foreach ($file in $manifest.files) {
        $target = [IO.Path]::GetFullPath((Join-Path $PackageRoot $file.path))
        if (-not $target.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid package path.' }
        if (-not (Test-Path -LiteralPath $target -PathType Leaf) -or
            (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $file.sha256) {
            throw "Package integrity check failed: $($file.path)"
        }
    }
    return $manifest
}

function Get-VerifiedDownload {
    param([Parameter(Mandatory)]$Dependency, [Parameter(Mandatory)][string]$Directory)
    $null = New-Item -ItemType Directory -Path $Directory -Force
    $file = Join-Path $Directory ([IO.Path]::GetFileName(([Uri]$Dependency.url).AbsolutePath))
    if (-not (Test-Path -LiteralPath $file) -or (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $Dependency.sha256) {
        Write-Host "Téléchargement officiel : $($Dependency.url)"
        $temporary = $file + '.download'
        Invoke-WebRequest -UseBasicParsing -Uri $Dependency.url -OutFile $temporary
        if ((Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash -ne $Dependency.sha256) {
            Remove-Item -LiteralPath $temporary
            throw 'Dependency SHA256 verification failed.'
        }
        Move-Item -LiteralPath $temporary -Destination $file -Force
    }
    return $file
}

function Expand-AgentRuntime {
    param([Parameter(Mandatory)][string]$Archive, [Parameter(Mandatory)][string]$Destination, [switch]$OllamaCpuOnly)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $null = New-Item -ItemType Directory -Path $Destination -Force
    $root = [IO.Path]::GetFullPath($Destination).TrimEnd('\') + '\'
    $zip = [IO.Compression.ZipFile]::OpenRead($Archive)
    try {
        foreach ($entry in $zip.Entries) {
            $name = $entry.FullName.Replace('\', '/')
            if ($name.EndsWith('/')) { continue }
            if ($OllamaCpuOnly -and $name -ne 'ollama.exe' -and $name -notmatch '^lib/ollama/[^/]+\.dll$') { continue }
            $target = [IO.Path]::GetFullPath((Join-Path $Destination $name))
            if (-not $target.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid archive path.' }
            $null = New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
        }
    } finally { $zip.Dispose() }
}
