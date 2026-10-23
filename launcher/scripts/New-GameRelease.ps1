[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BuildDirectory,
    [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9._-]{0,63}$')][string]$Version,
    [Parameter(Mandatory)][uri]$PackageUrl,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$Executable = 'Arkya.exe',
    [string]$ReleaseNotes = '',
    [ValidateSet(0, 1)][int]$LauncherSessionProtocol = 0
)
$ErrorActionPreference = 'Stop'
if ($PackageUrl.Scheme -ne 'https' -or $PackageUrl.UserInfo -or $PackageUrl.Fragment) {
    throw 'PackageUrl doit être une adresse HTTPS sans identifiants ni fragment.'
}
if ($Executable -notmatch '^[a-zA-Z0-9_-]+\.exe$') { throw 'Executable doit être un nom .exe situé à la racine du build.' }
if ($ReleaseNotes.Length -gt 16000) { throw 'Notes de version trop longues.' }
$buildRoot = (Resolve-Path -LiteralPath $BuildDirectory).Path.TrimEnd('\', '/')
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
if ($outputRoot.Equals($buildRoot, [StringComparison]::OrdinalIgnoreCase) -or
    $outputRoot.StartsWith($buildRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Le dossier de publication doit être situé hors du build.'
}
foreach ($required in @($Executable, 'UnityPlayer.dll', ([IO.Path]::GetFileNameWithoutExtension($Executable) + '_Data'))) {
    if (-not (Test-Path -LiteralPath (Join-Path $buildRoot $required))) { throw "Build Unity incomplet : $required absent." }
}
if ((Get-Item -LiteralPath $buildRoot).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Le build ne peut pas être un lien.' }
$allItems = @(Get-ChildItem -LiteralPath $buildRoot -Recurse -Force)
if ($allItems | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw 'Un lien est présent dans le build.' }
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$archivePath = Join-Path $outputRoot "Arkya-$Version-win-x64.zip"
$manifestPath = Join-Path $outputRoot 'latest-win-x64.json'
if ((Test-Path -LiteralPath $archivePath) -or (Test-Path -LiteralPath $manifestPath)) { throw 'Cette publication existe déjà. Choisissez un nouveau dossier de sortie.' }
$files = @($allItems | Where-Object {
    -not $_.PSIsContainer -and $_.Extension -notin @('.pdb', '.log') -and
    $_.FullName -notmatch '(_BackUpThisFolder_ButDontShipItWithYourGame|_BurstDebugInformation_DoNotShip)([\\/]|$)'
})
[long]$unpackedSize = 0
$archive = [IO.Compression.ZipFile]::Open($archivePath, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in $files) {
        $relative = [IO.Path]::GetRelativePath($buildRoot, $file.FullName).Replace('\', '/')
        if ($relative.Length -gt 220) { throw "Chemin trop long pour le launcher : $relative" }
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $relative, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        $unpackedSize += $file.Length
    }
} finally { $archive.Dispose() }
$packageSize = (Get-Item -LiteralPath $archivePath).Length
if ($packageSize -gt 50GB -or $unpackedSize -gt 100GB -or $unpackedSize -eq 0) { throw 'La taille du build dépasse les limites du launcher.' }
$manifest = [ordered]@{
    schemaVersion = 1
    version = $Version
    platform = 'win-x64'
    packageUrl = $PackageUrl.AbsoluteUri
    packageSize = $packageSize
    unpackedSize = $unpackedSize
    sha256 = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    executable = $Executable
    releaseNotes = $ReleaseNotes
}
if ($LauncherSessionProtocol -ne 0) { $manifest['launcherSessionProtocol'] = $LauncherSessionProtocol }
$manifest | ConvertTo-Json | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM
[pscustomobject]@{ Archive = $archivePath; Manifest = $manifestPath; PackageSize = $packageSize; Sha256 = $manifest.sha256 }
