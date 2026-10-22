[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot ('../../artifacts/windows-launcher-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))),
    [string]$ManifestUrl = ''
)
$ErrorActionPreference = 'Stop'
if ($ManifestUrl) {
    $address = [uri]$ManifestUrl
    if (-not $address.IsAbsoluteUri -or $address.Scheme -ne 'https' -or $address.UserInfo -or $address.Fragment) {
        throw 'ManifestUrl doit être une adresse HTTPS sans identifiants ni fragment.'
    }
}
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputRoot) { throw 'Le dossier de sortie existe déjà. Choisissez un nouveau dossier pour préserver la publication précédente.' }
$publishRoot = Join-Path $outputRoot 'Arkya-Launcher'
$project = Join-Path $PSScriptRoot '../src/Arkya.Launcher/Arkya.Launcher.csproj'
dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $publishRoot
if ($LASTEXITCODE -ne 0) { throw 'La publication du launcher a échoué.' }
@{ manifestUrl = $ManifestUrl } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $publishRoot 'launcher-settings.json') -Encoding utf8NoBOM
$instructions = @'
ARKYA — LAUNCHER WINDOWS

Extrayez ce dossier puis ouvrez Arkya.Launcher.exe.
Le launcher installe le jeu dans votre profil Windows, sans droits administrateur.
Si le canal n'est pas encore configuré, saisissez uniquement l'adresse HTTPS
du manifeste fournie par l'équipe Arkya dans Paramètres de distribution.
Vérifiez les mises à jour, installez le jeu, puis choisissez Jouer.
Une connexion est nécessaire pour télécharger; une version déjà installée
peut être lancée même lorsque le serveur de distribution est indisponible.
La connexion aux services de jeu reste gérée par le client Arkya.
'@
$instructions | Set-Content -LiteralPath (Join-Path $publishRoot 'LISEZ-MOI.txt') -Encoding utf8NoBOM
$archivePath = Join-Path $outputRoot 'Arkya-Launcher-win-x64.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($publishRoot, $archivePath, [IO.Compression.CompressionLevel]::Optimal, $true)
Get-FileHash -LiteralPath $archivePath -Algorithm SHA256 | Format-List
Write-Output $archivePath
