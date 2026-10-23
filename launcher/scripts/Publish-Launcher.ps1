[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot ('../../artifacts/windows-launcher-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))),
    [string]$InnoSetupCompiler = $env:ARKYA_INNO_SETUP_COMPILER
)
$ErrorActionPreference = 'Stop'
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputRoot) { throw 'Le dossier de sortie existe déjà. Choisissez un nouveau dossier pour préserver la publication précédente.' }
$publishRoot = Join-Path $outputRoot 'Arkya-Launcher'
$project = Join-Path $PSScriptRoot '../src/Arkya.Launcher/Arkya.Launcher.csproj'
dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $publishRoot
if ($LASTEXITCODE -ne 0) { throw 'La publication du launcher a échoué.' }
$instructions = @'
ARKYA — LAUNCHER WINDOWS

Installez Arkya-Launcher-Setup.exe, puis ouvrez Arkya Launcher depuis le menu Démarrer.
L’assistant permet de choisir le dossier du launcher sans droits administrateur.
Le launcher permet ensuite de choisir un autre dossier pour installer le jeu.
L’archive ZIP reste disponible pour une utilisation portable : extrayez-la
puis ouvrez Arkya.Launcher.exe.
Connectez-vous avec Discord dans votre navigateur pour accéder au launcher.
Choisissez Bêta ou Stable, puis le dossier du jeu. Installez-le et cliquez sur Jouer.
Les adresses officielles sont fixes et chaque canal conserve son installation.
Stable reste indisponible tant qu'aucune version n'y est publiée.
La connexion est mémorisée pour cet utilisateur Windows et vérifiée au démarrage.
La version installée vérifie ses propres mises à jour et redémarre après installation.
Les préférences, la connexion et le jeu sont conservés lors des mises à jour.
Pour une version portable, utilisez le nouvel installateur proposé sur le site.
'@
$instructions | Set-Content -LiteralPath (Join-Path $publishRoot 'LISEZ-MOI.txt') -Encoding utf8NoBOM
$launcherVersion = ([xml](Get-Content -LiteralPath $project -Raw)).Project.PropertyGroup.Version
$installer = & (Join-Path $PSScriptRoot 'New-LauncherInstaller.ps1') -PublishDirectory $publishRoot -OutputDirectory $outputRoot -LauncherVersion $launcherVersion -InnoSetupCompiler $InnoSetupCompiler
$updateManifestPath = Join-Path $outputRoot 'latest-win-x64.json'
[ordered]@{
    schemaVersion = 1
    version = [string]$launcherVersion
    platform = 'win-x64'
    installerUrl = ('https://github.com/Hantse/arkya-services/releases/download/launcher-v' + $launcherVersion + '/Arkya-Launcher-Setup.exe')
    installerSize = $installer.Size
    sha256 = $installer.Sha256
} | ConvertTo-Json | Set-Content -LiteralPath $updateManifestPath -Encoding utf8NoBOM
$archivePath = Join-Path $outputRoot 'Arkya-Launcher-win-x64.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($publishRoot, $archivePath, [IO.Compression.CompressionLevel]::Optimal, $true)
Get-FileHash -LiteralPath $archivePath -Algorithm SHA256 | Format-List
Write-Output $installer
Write-Output $archivePath
Write-Output $updateManifestPath
