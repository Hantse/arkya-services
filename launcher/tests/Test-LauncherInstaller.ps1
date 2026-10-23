[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$InstallerPath,
    [Parameter(Mandatory)][string]$PublishDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$installer = (Resolve-Path -LiteralPath $InstallerPath).Path
$publishRoot = (Resolve-Path -LiteralPath $PublishDirectory).Path
$testRoot = [IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\', '/')
if (Test-Path -LiteralPath $testRoot) { throw 'Le dossier de vérification doit être nouveau.' }
$uninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{9E8A9F67-445E-4876-B986-1C6D1E4ED1A0}_is1'
foreach ($hive in @([Microsoft.Win32.RegistryHive]::CurrentUser, [Microsoft.Win32.RegistryHive]::LocalMachine)) {
    foreach ($view in @([Microsoft.Win32.RegistryView]::Registry32, [Microsoft.Win32.RegistryView]::Registry64)) {
        $baseKey = [Microsoft.Win32.RegistryKey]::OpenBaseKey($hive, $view)
        try {
            $existing = $baseKey.OpenSubKey($uninstallKey)
            if ($existing) { $existing.Dispose(); throw 'Arkya Launcher est déjà installé. Le test ne modifiera pas cette installation.' }
        } finally { $baseKey.Dispose() }
    }
}
New-Item -ItemType Directory -Path $testRoot | Out-Null
$installRoot = Join-Path $testRoot 'chosen launcher folder'
$gameRoot = Join-Path $testRoot 'chosen game folder'
New-Item -ItemType Directory -Path $gameRoot | Out-Null
$gameMarker = Join-Path $gameRoot 'preserved-game.txt'
[IO.File]::WriteAllText($gameMarker, 'User-owned game data must survive launcher uninstall.')
$arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/NOICONS', '/TASKS=""', '/NOCLOSEAPPLICATIONS', '/NORESTARTAPPLICATIONS', ('/DIR="' + $installRoot + '"'))
function Invoke-InstallerProcess([string]$Path, [string[]]$Arguments) {
    $process = Start-Process -FilePath $Path -ArgumentList $Arguments -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(60000)) { throw ('Installer process timed out: ' + $Path) }
    if ($process.ExitCode -ne 0) { throw ('Installer process failed with exit code ' + $process.ExitCode) }
}
$installed = $false
try {
    $createdMutex = $false
    $runningAppMutex = [Threading.Mutex]::new($false, 'Local\Arkya.Launcher', [ref]$createdMutex)
    try {
        if (-not $createdMutex) { throw 'The launcher is already running. Close it before testing the installer.' }
        $blockedProcess = Start-Process -FilePath $installer -ArgumentList ($arguments + ('/LOG="' + (Join-Path $testRoot 'running-app-refused.log') + '"')) -WindowStyle Hidden -PassThru
        if (-not $blockedProcess.WaitForExit(60000) -or $blockedProcess.ExitCode -eq 0) {
            throw 'The installer did not refuse the running launcher mutex.'
        }
        if (Test-Path -LiteralPath (Join-Path $installRoot 'Arkya.Launcher.exe')) {
            throw 'The installer wrote the payload while the launcher was running.'
        }
    } finally { $runningAppMutex.Dispose() }
    Invoke-InstallerProcess $installer ($arguments + ('/LOG="' + (Join-Path $testRoot 'install.log') + '"'))
    $installed = $true
    $sourceFiles = @(Get-ChildItem -LiteralPath $publishRoot -File -Recurse)
    foreach ($source in $sourceFiles) {
        $relative = [IO.Path]::GetRelativePath($publishRoot, $source.FullName)
        $destination = Join-Path $installRoot $relative
        if (-not (Test-Path -LiteralPath $destination -PathType Leaf) -or
            (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $source.FullName -Algorithm SHA256).Hash) {
            throw ('Installed payload differs: ' + $relative)
        }
    }
    $registryPath = 'HKCU:\' + $uninstallKey
    $registration = Get-ItemProperty -LiteralPath $registryPath
    if ($registration.DisplayName -ne 'Arkya Launcher' -or
        -not $registration.InstallLocation.TrimEnd('\').Equals($installRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The installed application registration does not match the chosen location.'
    }
    # Unknown user files inside the launcher directory must also survive.
    $preferenceMarker = Join-Path $installRoot 'launcher-preferences.user-fixture.json'
    [IO.File]::WriteAllText($preferenceMarker, '{"preserve":true}')
    Invoke-InstallerProcess $installer ($arguments + ('/LOG="' + (Join-Path $testRoot 'reinstall.log') + '"'))
    if (-not (Test-Path -LiteralPath $preferenceMarker)) { throw 'Reinstall removed user preferences.' }
    $uninstaller = Join-Path $installRoot 'unins000.exe'
    Invoke-InstallerProcess $uninstaller @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', ('/LOG="' + (Join-Path $testRoot 'uninstall.log') + '"'))
    $installed = $false
    if ((Test-Path -LiteralPath $registryPath) -or
        (Test-Path -LiteralPath (Join-Path $installRoot 'Arkya.Launcher.exe'))) {
        throw 'Launcher uninstall did not remove its executable and registration.'
    }
    if (-not (Test-Path -LiteralPath $gameMarker) -or
        -not (Test-Path -LiteralPath $preferenceMarker)) {
        throw 'Launcher uninstall removed user game files or preferences.'
    }
    $verification = [pscustomobject]@{
        Installer = $installer
        InstallerSha256 = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
        VerifiedPayloadFiles = $sourceFiles.Count
        ChosenLauncherDirectory = $installRoot
        PerUserRegistration = $true
        RunningLauncherRefused = $true
        Reinstall = 'passed'
        Uninstall = 'passed'
        GamePreserved = $true
        PreferencesPreserved = $true
        LauncherExecuted = $false
        VerifiedAt = [DateTime]::UtcNow.ToString('o')
    }
    $verification | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $testRoot 'verification.json') -Encoding utf8NoBOM
    $verification
} finally {
    if ($installed) {
        $uninstaller = Join-Path $installRoot 'unins000.exe'
        if (Test-Path -LiteralPath $uninstaller -PathType Leaf) {
            Invoke-InstallerProcess $uninstaller @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', ('/LOG="' + (Join-Path $testRoot 'cleanup-uninstall.log') + '"'))
        }
    }
}
