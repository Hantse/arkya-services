[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PublishDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')][string]$LauncherVersion,
    [string]$InnoSetupCompiler = $env:ARKYA_INNO_SETUP_COMPILER
)
$ErrorActionPreference = 'Stop'
$publishRoot = [IO.Path]::GetFullPath($PublishDirectory).TrimEnd('\', '/')
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\', '/')
if (-not (Test-Path -LiteralPath (Join-Path $publishRoot 'Arkya.Launcher.exe') -PathType Leaf) -or
    -not (Test-Path -LiteralPath (Join-Path $publishRoot 'LISEZ-MOI.txt') -PathType Leaf)) {
    throw 'The launcher payload is incomplete. Publish the self-contained application first.'
}
if ($outputRoot.Equals($publishRoot, [StringComparison]::OrdinalIgnoreCase) -or
    $outputRoot.StartsWith($publishRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Installer output must remain outside the payload.'
}
if (@(Get-Item -LiteralPath $publishRoot; Get-ChildItem -LiteralPath $publishRoot -Force -Recurse) |
    Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) {
    throw 'Le payload ne doit pas contenir de liens.'
}
$setupPath = Join-Path $outputRoot 'Arkya-Launcher-Setup.exe'
if (Test-Path -LiteralPath $setupPath) { throw 'The installer already exists. Choose a new output.' }
if (-not $InnoSetupCompiler) {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) { $InnoSetupCompiler = $command.Source }
}
if (-not $InnoSetupCompiler -or -not (Test-Path -LiteralPath $InnoSetupCompiler -PathType Leaf)) {
    throw 'Inno Setup est requis. Fournissez -InnoSetupCompiler ou ARKYA_INNO_SETUP_COMPILER avec le chemin de ISCC.exe.'
}
$compiler = [IO.Path]::GetFullPath($InnoSetupCompiler)
$definition = Join-Path $PSScriptRoot '../installer/Arkya.Launcher.iss'
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
& $compiler '/Qp' ('/O' + $outputRoot) ('/DPayloadDirectory=' + $publishRoot) ('/DLauncherVersion=' + $LauncherVersion) $definition | Write-Host
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $setupPath -PathType Leaf)) {
    throw 'Installer compilation failed.'
}
[pscustomobject]@{
    Installer = $setupPath
    Version = $LauncherVersion
    Size = (Get-Item -LiteralPath $setupPath).Length
    Sha256 = (Get-FileHash -LiteralPath $setupPath -Algorithm SHA256).Hash.ToLowerInvariant()
}
