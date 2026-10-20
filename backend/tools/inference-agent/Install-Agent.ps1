[CmdletBinding()]
param(
    [string]$BackendUrl,
    [ValidatePattern('^[a-z][a-z0-9-]{1,46}[a-z0-9]$')][string]$AgentId,
    [string]$DisplayName,
    [Security.SecureString]$AgentToken,
    [Security.SecureString]$EnrollmentToken,
    [string]$InstallDirectory,
    [string]$ModelCache,
    [ValidateRange(1024,65535)][int]$DemoPort = 8767,
    [ValidateRange(1024,65524)][int]$BaseWorkerPort = 11441,
    [ValidateRange(0,12)][int]$InitialWorkers = 3,
    [ValidateRange(1,24)][int]$ThreadsPerWorker = 4,
    [ValidateRange(1,12)][int]$MaxWorkers = 6,
    [ValidateSet('lfm','qwen35-08b')][string]$ModelId = 'lfm',
    [switch]$RunAtLogon,
    [switch]$NoAutoStart,
    [switch]$SkipScheduledTask,
    [switch]$AllowLoopbackHttp
)
. (Join-Path $PSScriptRoot 'Agent.Common.ps1')
if (-not $AgentId) {
    $hostSlug = [regex]::Replace($env:COMPUTERNAME.ToLowerInvariant(),'[^a-z0-9-]','-').Trim('-')
    if (-not $hostSlug) { $hostSlug = 'machine' }
    $AgentId = 'pc-' + $hostSlug.Substring(0,[Math]::Min(45,$hostSlug.Length)).TrimEnd('-')
}
if (-not $PSBoundParameters.ContainsKey('DisplayName')) { $DisplayName = $env:COMPUTERNAME }
Assert-AgentEnrollmentIdentity -AgentId $AgentId -DisplayName $DisplayName
if (-not [Environment]::Is64BitOperatingSystem -or $env:PROCESSOR_ARCHITECTURE -eq 'ARM64') {
    throw 'This package targets Windows x64.'
}
$package = Assert-AgentPackage $PSScriptRoot
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
$isAdmin = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
$systemStartup = $isAdmin -and -not $RunAtLogon -and -not $SkipScheduledTask
$systemRoot = [IO.Path]::GetFullPath((Join-Path $env:ProgramData 'ArkyaInference')).TrimEnd('\')
if (-not $InstallDirectory) {
    $InstallDirectory = if ($systemStartup) { $systemRoot } else { Join-Path $env:LOCALAPPDATA 'ArkyaInference' }
}
$installRoot = [IO.Path]::GetFullPath($InstallDirectory).TrimEnd('\')
if (Test-Path -LiteralPath $installRoot) {
    Assert-AgentNoReparsePoints $installRoot
    if ($systemStartup) { Assert-AgentSystemTrust $installRoot }
}
$previousInstallation = $null
$installationPath = Join-Path $installRoot 'config/installation.json'
if (Test-Path -LiteralPath $installationPath) {
    $previousInstallation = Get-Content -Raw -Encoding UTF8 -LiteralPath $installationPath | ConvertFrom-Json
    if ($SkipScheduledTask -and $previousInstallation.startupMode -eq 'system-startup') {
        if (-not $isAdmin) { throw 'Updating a SYSTEM installation requires an elevated PowerShell.' }
        Assert-AgentSystemTrust $installRoot
        $systemStartup = $true
    }
}
if ($systemStartup -and $installRoot -ne $systemRoot) { throw 'SYSTEM startup requires the protected ProgramData\ArkyaInference directory. Use -RunAtLogon for a custom directory.' }
if (Test-Path -LiteralPath $installRoot) {
    $reparse = Get-Item -LiteralPath $installRoot -Force
    if ($reparse.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Installation directory must not be a reparse point.' }
    if ($systemStartup -and -not (Test-Path -LiteralPath (Join-Path $installRoot 'config/installation.json'))) {
        throw 'SYSTEM installation target already exists without Arkya installation metadata. Choose -RunAtLogon or remove the stale directory manually.'
    }
}
if ($installRoot -eq [IO.Path]::GetPathRoot($installRoot).TrimEnd('\')) { throw 'Choose a dedicated installation directory.' }
if ($BaseWorkerPort -le $DemoPort -and $DemoPort -lt ($BaseWorkerPort + 12)) { throw 'Demo port conflicts with worker range.' }
if ($BackendUrl) {
    $backend = [Uri]$BackendUrl
    $loopbackHttp = $AllowLoopbackHttp -and $backend.IsLoopback -and $backend.Scheme -eq 'http'
    if (-not $backend.IsAbsoluteUri -or ($backend.Scheme -ne 'https' -and -not $loopbackHttp) -or
        $backend.UserInfo -or $backend.Query -or $backend.Fragment) { throw 'BackendUrl must use HTTPS (HTTP loopback tests require -AllowLoopbackHttp).' }
    $BackendUrl = $BackendUrl.TrimEnd('/')
}
if (Test-Path -LiteralPath (Join-Path $installRoot 'Agent.Common.ps1')) {
    Stop-OwnedAgent $installRoot
}
Set-AgentPrivateDirectory $installRoot -SystemOnly:$systemStartup
$configRoot = Join-Path $installRoot 'config'
Set-AgentPrivateDirectory $configRoot -SystemOnly:$systemStartup
$previousConfig = $null
if (Test-Path -LiteralPath (Join-Path $configRoot 'agent.json')) {
    $previousConfig = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $configRoot 'agent.json') | ConvertFrom-Json
    foreach ($setting in @('AgentId','BackendUrl','DisplayName','ModelCache','DemoPort','BaseWorkerPort','InitialWorkers','ThreadsPerWorker','MaxWorkers','ModelId')) {
        if (-not $PSBoundParameters.ContainsKey($setting)) {
            $property = $setting.Substring(0,1).ToLowerInvariant() + $setting.Substring(1)
            if ($previousConfig.PSObject.Properties[$property]) { Set-Variable -Name $setting -Value $previousConfig.$property }
        }
    }
    if (-not $PSBoundParameters.ContainsKey('AllowLoopbackHttp')) { $AllowLoopbackHttp = [bool]$previousConfig.allowLoopbackHttp }
}
elseif (Test-Path -LiteralPath (Join-Path $configRoot 'enrollment-state.json')) {
    $pendingEnrollment = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $configRoot 'enrollment-state.json') | ConvertFrom-Json
    foreach ($setting in @('AgentId','BackendUrl','DisplayName')) {
        if (-not $PSBoundParameters.ContainsKey($setting)) {
            $property = $setting.Substring(0,1).ToLowerInvariant() + $setting.Substring(1)
            Set-Variable -Name $setting -Value $pendingEnrollment.$property
        }
    }
}
if ($InitialWorkers * $ThreadsPerWorker -gt 96) { throw 'Initial CPU thread allocation exceeds 96.' }
Assert-AgentEnrollmentIdentity -AgentId $AgentId -DisplayName $DisplayName
if ($InitialWorkers -gt $MaxWorkers) { throw 'InitialWorkers exceeds MaxWorkers.' }
if ($BaseWorkerPort -le $DemoPort -and $DemoPort -lt ($BaseWorkerPort + 12)) { throw 'Demo port conflicts with worker range.' }
if ($BackendUrl) {
    $backend = [Uri]$BackendUrl
    $loopbackHttp = $AllowLoopbackHttp -and $backend.IsLoopback -and $backend.Scheme -eq 'http'
    if (-not $backend.IsAbsoluteUri -or ($backend.Scheme -ne 'https' -and -not $loopbackHttp) -or
        $backend.UserInfo -or $backend.Query -or $backend.Fragment) { throw 'BackendUrl must use HTTPS (HTTP loopback tests require -AllowLoopbackHttp).' }
    $BackendUrl = $BackendUrl.TrimEnd('/')
}
foreach ($file in $package.files) {
    $source = Join-Path $PSScriptRoot $file.path
    $destination = Join-Path $installRoot $file.path
    if ([IO.Path]::GetFullPath($source) -eq [IO.Path]::GetFullPath($destination)) { continue }
    $null = New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($destination)) -Force
    Copy-Item -LiteralPath $source -Destination $destination -Force
}
if ([IO.Path]::GetFullPath($PSScriptRoot) -ne $installRoot) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'package-manifest.json') -Destination $installRoot -Force
}
$dependencies = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $installRoot 'dependencies.json') | ConvertFrom-Json
foreach ($runtime in @('python','ollama')) {
    $executable = Join-Path $installRoot "runtime/$runtime/$runtime.exe"
    if (-not (Test-Path -LiteralPath $executable)) {
        $archive = Get-VerifiedDownload $dependencies.$runtime (Join-Path $installRoot 'downloads')
        Expand-AgentRuntime -Archive $archive -Destination (Join-Path $installRoot "runtime/$runtime") -OllamaCpuOnly:($runtime -eq 'ollama')
    }
}
$pythonPath = Join-Path $installRoot 'runtime/python/python313._pth'
# Embeddable Python is isolated; add the application directory without enabling user site packages.
[IO.File]::WriteAllText($pythonPath, "python313.zip`n.`n../..`n", [Text.UTF8Encoding]::new($false))
if (-not $ModelCache) { $ModelCache = Join-Path $installRoot 'models' }
$ModelCache = [IO.Path]::GetFullPath($ModelCache)
if ($systemStartup -and $ModelCache -ne (Join-Path $installRoot 'models')) { throw 'SYSTEM startup uses its private models directory. Use -RunAtLogon for a custom cache.' }
if (Test-Path -LiteralPath $ModelCache) { Assert-AgentNoReparsePoints $ModelCache }
Set-AgentPrivateDirectory $ModelCache -SystemOnly:$systemStartup
$tokenFile = Join-Path $configRoot 'agent-token.dpapi'
if ($BackendUrl) {
    $sameRegistration = $null -ne $previousConfig -and $previousConfig.agentId -eq $AgentId -and $previousConfig.backendUrl -eq $BackendUrl
    if ($AgentToken) {
        Write-AgentCredential -Path $tokenFile -Secret $AgentToken
    } elseif (-not $sameRegistration -or -not (Test-Path -LiteralPath $tokenFile)) {
        # Preserve resumable installer identity even if enrollment completes remotely then times out.
        if (-not (Test-Path -LiteralPath (Join-Path $configRoot 'installation.json'))) {
            $pendingTaskName = 'ArkyaInference-' + ([BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash([Text.Encoding]::UTF8.GetBytes($installRoot.ToLowerInvariant()))).Replace('-','').Substring(0,12))
            [IO.File]::WriteAllText((Join-Path $configRoot 'installation.json'), (@{taskName=$pendingTaskName;startupMode='manual';installRoot=$installRoot} | ConvertTo-Json),[Text.UTF8Encoding]::new($false))
        }
        if ($systemStartup) {
            & icacls.exe $installRoot /setowner '*S-1-5-32-544' /T /Q | Out-Null
            if ($LASTEXITCODE -ne 0) { throw 'Cannot secure resumable SYSTEM installation.' }
            Assert-AgentSystemTrust $installRoot
        }
        try {
            Register-AgentEnrollment -BackendUrl $BackendUrl -AgentId $AgentId -DisplayName $DisplayName -ConfigDirectory $configRoot `
                -EnrollmentToken $EnrollmentToken -InitialWorkers $InitialWorkers -ThreadsPerWorker $ThreadsPerWorker `
                -MaxWorkers $MaxWorkers -ModelId $ModelId -AllowLoopbackHttp:$AllowLoopbackHttp -SystemOnly:$systemStartup
        } catch {
            [Console]::Error.WriteLine($_.Exception.Message)
            exit 1
        }
    }
    if ((-not $sameRegistration -or $AgentToken) -and (Test-Path -LiteralPath (Join-Path $configRoot 'desired.json'))) {
        Remove-Item -LiteralPath (Join-Path $configRoot 'desired.json')
    }
}
$configuration = [ordered]@{
    schemaVersion = 1; agentId = $AgentId; backendUrl = $BackendUrl; tokenFile = $tokenFile
    displayName = $DisplayName; maxWorkers = $MaxWorkers; modelId = $ModelId
    ollamaExecutable = (Join-Path $installRoot 'runtime/ollama/ollama.exe'); modelCache = $ModelCache
    logsDirectory = (Join-Path $installRoot 'logs'); demoPort = $DemoPort; baseWorkerPort = $BaseWorkerPort
    initialWorkers = $InitialWorkers; threadsPerWorker = $ThreadsPerWorker; allowLoopbackHttp = $AllowLoopbackHttp.IsPresent
}
[IO.File]::WriteAllText((Join-Path $configRoot 'agent.json'), ($configuration | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
$taskName = 'ArkyaInference-' + ([BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash([Text.Encoding]::UTF8.GetBytes($installRoot.ToLowerInvariant()))).Replace('-','').Substring(0,12))
$startupMode = if ($SkipScheduledTask -and $previousInstallation) { $previousInstallation.startupMode } else { 'manual' }
if ($SkipScheduledTask -and $previousInstallation) { $taskName = $previousInstallation.taskName }
if (-not $SkipScheduledTask) {
    $shell = Join-Path $env:WINDIR 'System32/WindowsPowerShell/v1.0/powershell.exe'
    $arguments = '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + (Join-Path $installRoot 'Start-Agent.ps1') + '" -InstallDirectory "' + $installRoot + '" -Wait'
    $action = New-ScheduledTaskAction -Execute $shell -Argument $arguments -WorkingDirectory $installRoot
    if ($systemStartup) {
        $trigger = New-ScheduledTaskTrigger -AtStartup
        $taskPrincipal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
        $startupMode = 'system-startup'
    } else {
        $userId = [Security.Principal.WindowsIdentity]::GetCurrent().Name
        $trigger = New-ScheduledTaskTrigger -AtLogOn -User $userId
        $taskPrincipal = New-ScheduledTaskPrincipal -UserId $userId -LogonType Interactive -RunLevel Limited
        $startupMode = 'user-logon'
    }
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable `
        -MultipleInstances IgnoreNew -ExecutionTimeLimit ([TimeSpan]::Zero) -Hidden -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1)
    # Register only after the final ownership/integrity checks below.
}
[IO.File]::WriteAllText((Join-Path $configRoot 'installation.json'), (@{taskName=$taskName; startupMode=$startupMode; installRoot=$installRoot} | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
# Download-only packages gain the same startup integrity checks as bundled runtimes.
$installedManifest = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $installRoot 'package-manifest.json') | ConvertFrom-Json
$knownFiles = @{}
foreach ($file in $installedManifest.files) { $knownFiles[$file.path] = $true }
$additionalFiles = @(foreach ($file in Get-ChildItem -LiteralPath (Join-Path $installRoot 'runtime') -Recurse -File) {
    $relative = $file.FullName.Substring($installRoot.Length + 1).Replace('\','/')
    if (-not $knownFiles.ContainsKey($relative)) {
        @{path=$relative; sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant(); bytes=$file.Length}
    }
})
$installedManifest.files = @($installedManifest.files) + $additionalFiles
[IO.File]::WriteAllText((Join-Path $installRoot 'package-manifest.json'), ($installedManifest | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
if ($systemStartup) {
    # An unelevated owner SID must not be able to rewrite SYSTEM's payload through WRITE_DAC.
    & icacls.exe $installRoot /setowner '*S-1-5-32-544' /T /Q | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Cannot secure SYSTEM installation ownership.' }
    Assert-AgentSystemTrust $installRoot
}
if (-not $SkipScheduledTask) {
    Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $taskPrincipal -Settings $settings -Force | Out-Null
}
Write-Output "Installé dans $installRoot · démarrage automatique : $startupMode"
if (-not $NoAutoStart) {
    if ($startupMode -eq 'manual') { & (Join-Path $installRoot 'Start-Agent.ps1') -InstallDirectory $installRoot }
    else {
        Start-ScheduledTask -TaskName $taskName
        Write-Output "Tâche démarrée. Démo : http://127.0.0.1:$DemoPort"
    }
}
