[CmdletBinding()]
param(
    [string] $OutputDirectory = (Join-Path $PSScriptRoot '.local')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Read-PrivateValue([string] $Prompt) {
    $secure = Read-Host -Prompt $Prompt -AsSecureString
    $pointer = [IntPtr]::Zero
    try {
        $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
        $value = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
        if ([string]::IsNullOrWhiteSpace($value) -or $value -match '[\r\n\0]') {
            throw 'La valeur doit tenir sur une seule ligne non vide.'
        }
        return $value
    }
    finally {
        if ($pointer -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
        $secure.Dispose()
    }
}

function New-ServiceKey {
    $bytes = New-Object byte[] 32
    $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $generator.GetBytes($bytes) }
    finally { $generator.Dispose() }
    return [Convert]::ToBase64String($bytes)
}

$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
$names = @('admin.env', 'content.env', 'realms.env')
foreach ($name in $names) {
    if (Test-Path -LiteralPath (Join-Path $outputPath $name)) {
        throw "Le fichier $name existe deja. Preparation annulee pour conserver les secrets existants."
    }
}

$contentMongo = Read-PrivateValue 'URI MongoDB pour arkya_content (saisie masquee)'
$realmsMongo = Read-PrivateValue 'URI MongoDB pour arkya_realms (saisie masquee)'
foreach ($connection in @($contentMongo, $realmsMongo)) {
    if ($connection -notmatch '^mongodb(\+srv)?://\S+$') {
        throw 'Chaque connexion MongoDB doit etre une URI mongodb:// ou mongodb+srv:// sans espace.'
    }
}
$discordClientId = Read-PrivateValue 'Client ID de l application Discord existante (saisie masquee)'
$parsedClientId = [UInt64]0
if ($discordClientId -notmatch '^[1-9][0-9]{16,19}$' -or
    -not [UInt64]::TryParse($discordClientId, [ref]$parsedClientId) -or $parsedClientId -eq 0) {
    throw 'Le Client ID Discord doit etre son identifiant numerique, pas le nom de l application.'
}
$discordClientSecret = Read-PrivateValue 'Client Secret de cette application Discord (saisie masquee)'
if ($discordClientSecret.Length -lt 16 -or $discordClientSecret.Length -gt 512) {
    throw 'Le Client Secret Discord doit contenir 16 a 512 caracteres.'
}

$contentAdminKey = New-ServiceKey
$realmsAdminKey = New-ServiceKey
$files = [ordered]@{
    'admin.env' = @(
        "Discord__ClientId=$discordClientId"
        "Discord__ClientSecret=$discordClientSecret"
        "ContentApi__AdminKey=$contentAdminKey"
        "RealmsApi__AdminKey=$realmsAdminKey"
    )
    'content.env' = @(
        "Mongo__ConnectionString=$contentMongo"
        "ContentAdmin__Key=$contentAdminKey"
        "ServiceAuthentication__Key=$(New-ServiceKey)"
    )
    'realms.env' = @(
        "Mongo__ConnectionString=$realmsMongo"
        "RealmsAdmin__Key=$realmsAdminKey"
        "GameAccess__ServiceKey=$(New-ServiceKey)"
    )
}

[void][IO.Directory]::CreateDirectory($outputPath)
$encoding = New-Object Text.UTF8Encoding($false)
foreach ($entry in $files.GetEnumerator()) {
    $path = Join-Path $outputPath $entry.Key
    # CreateNew also refuses a file created concurrently after the initial check.
    $stream = [IO.File]::Open($path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    $writer = New-Object IO.StreamWriter($stream, $encoding)
    try { $writer.Write(($entry.Value -join "`n") + "`n") }
    finally { $writer.Dispose() }
}
Write-Output "Trois fichiers prepares dans $outputPath : admin.env, content.env, realms.env."
Write-Output 'Les cles interservices sont generees et associees. Aucun secret affiche, aucun changement dans Kubernetes.'
