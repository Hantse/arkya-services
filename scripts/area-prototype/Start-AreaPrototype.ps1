[CmdletBinding()]
param(
    [string]$ExecutablePath,
    [string]$ServerExecutablePath,
    [string]$OutputRoot,
    [ValidateRange(10, 600)][int]$DurationSeconds = 45,
    [ValidateRange(0, 2000)][int]$PeerDelayMs = 0,
    [ValidateRange(0, 120)][int]$StartupLeadSeconds = 0,
    [ValidateSet('Direct3D11', 'Direct3D12', 'Automatic')][string]$GraphicsApi = 'Direct3D11',
    [switch]$Automated,
    [switch]$HeadlessClients,
    [switch]$DuplicateImpacts,
    [switch]$QuestScenario
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function ConvertTo-NativeArgument([string]$Value) {
    if ($Value.Length -gt 0 -and $Value -notmatch '[\s"]') { return $Value }
    $escaped = [regex]::Replace($Value, '(\\*)"', '$1$1\"')
    $escaped = [regex]::Replace($escaped, '(\\+)$', '$1$1')
    return '"' + $escaped + '"'
}

function Stop-OwnedWorker($Worker, [string]$ExpectedPath) {
    $recordedPath = $Worker.PSObject.Properties['ExecutablePath']
    if ($null -ne $recordedPath -and -not [string]::IsNullOrWhiteSpace($recordedPath.Value)) {
        $ExpectedPath = $recordedPath.Value
    }
    $workerProcess = Get-Process -Id $Worker.ProcessId -ErrorAction SilentlyContinue
    if ($null -eq $workerProcess) { return }
    try {
        if ($workerProcess.HasExited) { return }
        if ($workerProcess.Path -eq $ExpectedPath -and
            $workerProcess.StartTime.ToUniversalTime().Ticks -eq $Worker.StartTimeUtcTicks) {
            Stop-Process -Id $workerProcess.Id -ErrorAction SilentlyContinue
        }
    } catch {
        if (-not $workerProcess.HasExited) { throw }
    }
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if ([string]::IsNullOrWhiteSpace($ExecutablePath)) {
    $ExecutablePath = Join-Path $repositoryRoot 'artifacts/area-prototype/build/Arkya.AreaPrototype.exe'
}
$resolvedExecutable = (Resolve-Path -LiteralPath $ExecutablePath).Path
if (-not (Test-Path -LiteralPath $resolvedExecutable -PathType Leaf)) {
    throw "The prototype executable does not exist: $resolvedExecutable"
}
if ([string]::IsNullOrWhiteSpace($ServerExecutablePath)) {
    $defaultServerExecutable = Join-Path $repositoryRoot 'artifacts/area-prototype/build/server/Arkya.AreaPrototype.Server.exe'
    $ServerExecutablePath = if (Test-Path -LiteralPath $defaultServerExecutable -PathType Leaf) {
        $defaultServerExecutable
    } else {
        $resolvedExecutable
    }
}
$resolvedServerExecutable = (Resolve-Path -LiteralPath $ServerExecutablePath).Path
if (-not (Test-Path -LiteralPath $resolvedServerExecutable -PathType Leaf)) {
    throw "The prototype server executable does not exist: $resolvedServerExecutable"
}
$udpEndpointCommand = Get-Command Get-NetUDPEndpoint -ErrorAction SilentlyContinue
if ($null -ne $udpEndpointCommand) {
    $reservedPorts = @(7777, 7778, 7790, 7791)
    $occupiedEndpoints = @(Get-NetUDPEndpoint -ErrorAction Stop | Where-Object { $_.LocalPort -in $reservedPorts })
    if ($occupiedEndpoints.Count -gt 0) {
        $occupied = @($occupiedEndpoints | ForEach-Object { "UDP $($_.LocalPort) (PID $($_.OwningProcess))" } | Select-Object -Unique) -join ', '
        throw "Cannot start overlapping area-prototype processes: $occupied is already in use. Stop the previous run or free these ports before starting again."
    }
} else {
    Write-Warning 'Get-NetUDPEndpoint is unavailable; the UDP port preflight could not run.'
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repositoryRoot 'artifacts/area-prototype/runs'
}
$resolvedOutputRoot = [IO.Path]::GetFullPath($OutputRoot)
$runId = [Guid]::NewGuid().ToString('D')
$runDirectory = Join-Path $resolvedOutputRoot ((Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + $runId.Substring(0, 8))
New-Item -Path $runDirectory -ItemType Directory -Force | Out-Null
$startupSeconds = if ($StartupLeadSeconds -gt 0) { $StartupLeadSeconds } elseif ($Automated) { 20 } else { 8 }
$startUnixMs = [DateTimeOffset]::UtcNow.AddSeconds($startupSeconds).ToUnixTimeMilliseconds()
$workers = [Collections.Generic.List[object]]::new()
$workerDefinitions = @(
    @{ Role = 'server'; Index = 0 },
    @{ Role = 'server'; Index = 1 },
    @{ Role = 'client'; Index = 1 },
    @{ Role = 'client'; Index = 2 }
)

try {
    foreach ($definition in $workerDefinitions) {
        $workerName = "$($definition.Role)-$($definition.Index)"
        $arguments = [Collections.Generic.List[string]]::new()
        $arguments.AddRange([string[]]@(
            '-arkyaPrototype', '-arkyaRole', $definition.Role,
            '-arkyaRunId', $runId,
            '-arkyaResultDir', $runDirectory,
            '-arkyaStartUnixMs', $startUnixMs.ToString([Globalization.CultureInfo]::InvariantCulture),
            '-arkyaDuration', $DurationSeconds.ToString([Globalization.CultureInfo]::InvariantCulture),
            '-logFile', (Join-Path $runDirectory "$workerName.log")
        ))
        if ($definition.Role -eq 'server') {
            $arguments.AddRange([string[]]@('-arkyaArea', $definition.Index.ToString(), '-batchmode', '-nographics'))
            if ($DuplicateImpacts) { $arguments.Add('-arkyaDuplicateImpacts') }
            if ($PeerDelayMs -gt 0) {
                $arguments.AddRange([string[]]@('-arkyaPeerDelayMs', $PeerDelayMs.ToString()))
            }
        } else {
            $arguments.AddRange([string[]]@('-arkyaClient', $definition.Index.ToString()))
            if ($HeadlessClients) {
                $arguments.AddRange([string[]]@('-batchmode', '-nographics'))
            } else {
                $arguments.AddRange([string[]]@('-screen-fullscreen', '0', '-screen-width', '960', '-screen-height', '600'))
                if ($GraphicsApi -eq 'Direct3D11') { $arguments.Add('-force-d3d11') }
                elseif ($GraphicsApi -eq 'Direct3D12') { $arguments.Add('-force-d3d12') }
            }
        }
        if ($Automated) { $arguments.Add('-arkyaAutomated') }
        if ($QuestScenario) { $arguments.Add('-arkyaQuestScenario') }

        $workerExecutable = if ($definition.Role -eq 'server') { $resolvedServerExecutable } else { $resolvedExecutable }
        $hideWindow = $definition.Role -eq 'server' -or $HeadlessClients
        $workerStartInfo = [Diagnostics.ProcessStartInfo]::new()
        $workerStartInfo.FileName = $workerExecutable
        $workerStartInfo.Arguments = ($arguments | ForEach-Object { ConvertTo-NativeArgument $_ }) -join ' '
        $workerStartInfo.WorkingDirectory = [IO.Path]::GetDirectoryName($workerExecutable)
        $workerStartInfo.UseShellExecute = $false
        $workerStartInfo.CreateNoWindow = $hideWindow
        $workerStartInfo.WindowStyle = if ($hideWindow) { [Diagnostics.ProcessWindowStyle]::Hidden } else { [Diagnostics.ProcessWindowStyle]::Normal }
        if ($definition.Role -eq 'client') {
            # Filter only the child environment; ProcessStartInfo also supports Windows PowerShell 5.1.
            foreach ($variableName in @('ARKYA_CONTENT_SERVICE_KEY', 'ARKYA_CHARACTERS_SERVICE_KEY', 'ARKYA_CHARACTER_1_ID', 'ARKYA_CHARACTER_2_ID')) {
                $workerStartInfo.EnvironmentVariables.Remove($variableName)
            }
        }
        $workerProcess = [Diagnostics.Process]::Start($workerStartInfo)
        $workers.Add([pscustomobject]@{
            Name = $workerName
            Role = $definition.Role
            Index = $definition.Index
            ProcessId = $workerProcess.Id
            ExecutablePath = $workerExecutable
            StartTimeUtcTicks = $workerProcess.StartTime.ToUniversalTime().Ticks
            LogPath = Join-Path $runDirectory "$workerName.log"
        })
        Write-Host "Started $workerName (PID $($workerProcess.Id))."
    }
    $run = [pscustomobject]@{
        RunId = $runId
        RunDirectory = $runDirectory
        ExecutablePath = $resolvedExecutable
        ServerExecutablePath = $resolvedServerExecutable
        StartUnixMs = $startUnixMs
        StartupLeadSeconds = $startupSeconds
        DurationSeconds = $DurationSeconds
        Automated = [bool]$Automated
        HeadlessClients = [bool]$HeadlessClients
        GraphicsApi = $GraphicsApi
        DuplicateImpacts = [bool]$DuplicateImpacts
        QuestScenario = [bool]$QuestScenario
        PeerDelayMs = $PeerDelayMs
        Workers = $workers.ToArray()
    }
    $run | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $runDirectory 'run.json') -Encoding utf8
    Write-Host "Run reports: $runDirectory"
    return $run
} catch {
    foreach ($worker in $workers) { Stop-OwnedWorker $worker $resolvedExecutable }
    throw
}
