[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$LauncherCoreAssemblyPath = (Join-Path $PSScriptRoot '../src/Arkya.Launcher.Core/bin/Release/net10.0/Arkya.Launcher.Core.dll'),
    [string]$InnoSetupCompiler = $env:ARKYA_INNO_SETUP_COMPILER
)
$ErrorActionPreference = 'Stop'
$testRoot = [IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\', '/')
if (Test-Path -LiteralPath $testRoot) { throw 'Le dossier de vérification doit être nouveau.' }
$core = (Resolve-Path -LiteralPath $LauncherCoreAssemblyPath).Path
$compiler = (Resolve-Path -LiteralPath $InnoSetupCompiler).Path
New-Item -ItemType Directory -Path $testRoot | Out-Null
$fixtureGuid = [guid]::NewGuid().ToString().ToUpperInvariant()
$fixtureMutex = 'Local\Arkya.Launcher.UpdateFixture.' + $fixtureGuid
$projectRoot = Join-Path $testRoot 'fixture-project'
$payloadRoot = Join-Path $testRoot 'fixture-payload'
$installRoot = Join-Path $testRoot "custom launcher's folder"
$dataRoot = Join-Path $testRoot 'user-data'
$fixtureKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{' + $fixtureGuid + '}_is1'
$productionKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{9E8A9F67-445E-4876-B986-1C6D1E4ED1A0}_is1'
function Get-ProductionRegistration {
    if (Test-Path -LiteralPath $productionKey) {
        $registration = Get-ItemProperty -LiteralPath $productionKey
        return (@($registration.DisplayVersion, $registration.InstallLocation, $registration.UninstallString) | ConvertTo-Json -Compress)
    }
    return 'absent'
}
$productionBefore = Get-ProductionRegistration
New-Item -ItemType Directory -Path $projectRoot, $dataRoot | Out-Null
@'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>Arkya.Launcher</AssemblyName>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
'@ | Set-Content -LiteralPath (Join-Path $projectRoot 'UpdateFixture.csproj') -Encoding utf8NoBOM
@'
var root = AppContext.BaseDirectory;
using var mutex = new Mutex(false, File.ReadAllText(Path.Combine(root, "fixture-mutex.txt")));
if (args.Length > 0 && args[0] == "--parent")
{
    File.WriteAllText(Path.Combine(root, "parent-running.txt"), "running");
    var deadline = DateTime.UtcNow.AddSeconds(60);
    while (!File.Exists(Path.Combine(root, "exit-parent.flag")) && DateTime.UtcNow < deadline)
        Thread.Sleep(50);
    File.WriteAllText(Path.Combine(root, "parent-exited.txt"), "exited");
}
else File.WriteAllText(Path.Combine(root, "launcher-restarted.txt"), Environment.ProcessPath);
'@ | Set-Content -LiteralPath (Join-Path $projectRoot 'Program.cs') -Encoding utf8NoBOM
dotnet publish (Join-Path $projectRoot 'UpdateFixture.csproj') -c Release -r win-x64 --self-contained false -o $payloadRoot | Write-Host
if ($LASTEXITCODE -ne 0) { throw 'Fixture compilation failed.' }
[IO.File]::WriteAllText((Join-Path $payloadRoot 'fixture-mutex.txt'), $fixtureMutex)
$definitionSource = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../installer/Arkya.Launcher.iss')).Path
$definition = Get-Content -LiteralPath $definitionSource -Raw
$definition = $definition.Replace('9E8A9F67-445E-4876-B986-1C6D1E4ED1A0', $fixtureGuid)
$definition = $definition.Replace('AppMutex=Local\Arkya.Launcher', ('AppMutex=' + $fixtureMutex))
$definition = $definition.Replace('AppName=Arkya Launcher', 'AppName=Arkya Launcher Update Fixture')
$definition = $definition.Replace('DefaultGroupName=Arkya', ('DefaultGroupName=Arkya Update Fixture ' + $fixtureGuid))
$icon = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../src/Arkya.Launcher/Assets/Arkya.ico')).Path
$definition = $definition.Replace('SetupIconFile=..\src\Arkya.Launcher\Assets\Arkya.ico', ('SetupIconFile=' + $icon))
$fixtureDefinition = Join-Path $testRoot 'UpdateFixture.iss'
[IO.File]::WriteAllText($fixtureDefinition, $definition)
function New-FixtureInstaller([string]$Version, [string]$Destination) {
    New-Item -ItemType Directory -Path $Destination | Out-Null
    [IO.File]::WriteAllText((Join-Path $payloadRoot 'payload-version.txt'), $Version)
    & $compiler '/Qp' ('/O' + $Destination) ('/DPayloadDirectory=' + $payloadRoot) ('/DLauncherVersion=' + $Version) $fixtureDefinition | Write-Host
    if ($LASTEXITCODE -ne 0) { throw 'Fixture installer compilation failed.' }
    return (Join-Path $Destination 'Arkya-Launcher-Setup.exe')
}
function Invoke-FixtureInstaller([string]$Path, [string[]]$Arguments) {
    $process = Start-Process -FilePath $Path -ArgumentList $Arguments -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(60000) -or $process.ExitCode -ne 0) { throw 'Fixture installation failed.' }
}
function Wait-FixtureFile([string]$Path) {
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    while (-not (Test-Path -LiteralPath $Path)) {
        if ([DateTime]::UtcNow -ge $deadline) { throw ('Fixture marker did not appear: ' + $Path) }
        Start-Sleep -Milliseconds 100
    }
}
$oldSetup = New-FixtureInstaller '0.3.1' (Join-Path $testRoot 'initial-installer')
$updateSetup = New-FixtureInstaller '0.4.0' (Join-Path $testRoot 'update-installer')
$updateHash = (Get-FileHash -LiteralPath $updateSetup -Algorithm SHA256).Hash.ToLowerInvariant()
$protectedData = Join-Path ([IO.Path]::GetDirectoryName($core)) 'System.Security.Cryptography.ProtectedData.dll'
if (Test-Path -LiteralPath $protectedData) { [Reflection.Assembly]::LoadFrom($protectedData) | Out-Null }
[Reflection.Assembly]::LoadFrom($core) | Out-Null
$parent = $null
$installed = $false
try {
    Invoke-FixtureInstaller $oldSetup @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/NOICONS', '/TASKS=""', ('/DIR="' + $installRoot + '"'))
    $installed = $true
    $preferencePath = Join-Path $dataRoot 'launcher-preferences.json'
    [IO.File]::WriteAllText($preferencePath, '{"channel":"stable","gameDirectory":"custom-game-location"}')
    $store = [Arkya.Launcher.Core.ProtectedLauncherSessionStore]::new($dataRoot)
    $credential = [Arkya.Launcher.Core.LauncherRefreshCredential]::new(('a' * 32 + '.' + 'B' * 43), [guid]::NewGuid().ToString(), [DateTimeOffset]::UtcNow.AddDays(2))
    $store.Save($credential)
    $sessionPath = Join-Path $dataRoot 'launcher-session.dat'
    $sessionHash = (Get-FileHash -LiteralPath $sessionPath -Algorithm SHA256).Hash
    $preferencesHash = (Get-FileHash -LiteralPath $preferencePath -Algorithm SHA256).Hash
    $userFile = Join-Path $installRoot 'user-owned-preferences.json'
    [IO.File]::WriteAllText($userFile, '{"preserved":true}')
    $parent = Start-Process -FilePath (Join-Path $installRoot 'Arkya.Launcher.exe') -ArgumentList '--parent' -WindowStyle Hidden -PassThru
    Wait-FixtureFile (Join-Path $installRoot 'parent-running.txt')
    $manifest = [Arkya.Launcher.Core.LauncherUpdateManifest]::new(1, '0.4.0', 'win-x64',
        'https://github.com/Hantse/arkya-services/releases/download/launcher-v0.4.0/Arkya-Launcher-Setup.exe',
        (Get-Item -LiteralPath $updateSetup).Length, $updateHash)
    $method = [Arkya.Launcher.Core.LauncherUpdateInstaller].GetMethod('CreateStartInfo', [Reflection.BindingFlags]'NonPublic,Static')
    $createStartInfo = $method.CreateDelegate([Func[Arkya.Launcher.Core.LauncherUpdateManifest,string,string,int,long,Diagnostics.ProcessStartInfo]])
    $start = $createStartInfo.Invoke($manifest, [string]$updateSetup, $installRoot, $parent.Id, $parent.StartTime.ToUniversalTime().ToFileTimeUtc())
    $helper = [Diagnostics.Process]::Start($start)
    Start-Sleep -Milliseconds 1200
    if ($parent.HasExited -or $helper.HasExited -or (Get-Content -LiteralPath (Join-Path $installRoot 'payload-version.txt') -Raw) -ne '0.3.1') {
        throw 'The update did not wait for its exact parent to exit.'
    }
    [IO.File]::WriteAllText((Join-Path $installRoot 'exit-parent.flag'), 'exit')
    if (-not $parent.WaitForExit(15000)) { throw 'The fixture parent did not exit.' }
    if (-not $helper.WaitForExit(60000) -or $helper.ExitCode -ne 0) {
        $errorPath = $updateSetup + '.error.txt'
        if (Test-Path -LiteralPath $errorPath) { throw (Get-Content -LiteralPath $errorPath -Raw) }
        throw 'The real update helper failed.'
    }
    Wait-FixtureFile (Join-Path $installRoot 'launcher-restarted.txt')
    if ((Get-Content -LiteralPath (Join-Path $installRoot 'payload-version.txt') -Raw) -ne '0.4.0' -or
        (Get-ItemProperty -LiteralPath $fixtureKey).DisplayVersion -ne '0.4.0') { throw 'The new version was not installed.' }
    if ((Get-Content -LiteralPath (Join-Path $installRoot 'launcher-restarted.txt') -Raw) -ne (Join-Path $installRoot 'Arkya.Launcher.exe')) {
        throw 'The helper did not restart the launcher from the chosen directory.'
    }
    if ((Get-FileHash -LiteralPath $sessionPath -Algorithm SHA256).Hash -ne $sessionHash -or
        (Get-FileHash -LiteralPath $preferencePath -Algorithm SHA256).Hash -ne $preferencesHash -or
        -not (Test-Path -LiteralPath $userFile) -or $store.Load().AccountId -ne $credential.AccountId) {
        throw 'Update changed user preferences or the encrypted session.'
    }
    if ((Get-ProductionRegistration) -ne $productionBefore) { throw 'The real Arkya installation registration changed.' }
    $verification = [ordered]@{
        CoreAssembly = $core
        CoreSha256 = (Get-FileHash -LiteralPath $core -Algorithm SHA256).Hash.ToLowerInvariant()
        FixtureAppId = $fixtureGuid
        FixtureInstallerSha256 = $updateHash
        ParentWait = 'passed'
        SilentUpdate = '0.3.1 -> 0.4.0'
        CustomDirectory = $installRoot
        RestartedLauncher = 'passed'
        PreferencesPreserved = $true
        EncryptedSessionPreserved = $true
        EncryptedSessionReadable = $true
        UnknownUserFilePreserved = $true
        ProductionRegistrationPreserved = $true
        ActualGameOrLauncherExecuted = $false
        VerifiedAt = [DateTimeOffset]::UtcNow.ToString('o')
    }
    $verification | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $testRoot 'verification.json') -Encoding utf8NoBOM
    [pscustomobject]$verification
} finally {
    if ($parent -and -not $parent.HasExited) {
        [IO.File]::WriteAllText((Join-Path $installRoot 'exit-parent.flag'), 'exit')
        $parent.WaitForExit(15000) | Out-Null
    }
    if ($installed -and (Test-Path -LiteralPath $fixtureKey)) {
        $registeredRoot = (Get-ItemProperty -LiteralPath $fixtureKey).InstallLocation.TrimEnd('\')
        if (-not $registeredRoot.Equals($installRoot, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Refusing cleanup outside the exact fixture installation.'
        }
        Invoke-FixtureInstaller (Join-Path $installRoot 'unins000.exe') @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART')
    }
}
