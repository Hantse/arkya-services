#requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ServerExecutable,
    [string]$RunDirectory = '',
    [string]$SourceProject = '',
    [string]$PythonExecutable = 'python',
    [ValidateRange(20, 600)][int]$TimeoutSeconds = 90,
    [ValidateRange(6, 120)][int]$ObservationSeconds = 10
)
$ErrorActionPreference = 'Stop'
if (!$IsWindows) { throw 'This harness requires Windows dedicated executables and Windows TCP process ownership inspection.' }
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (!$SourceProject) { $SourceProject = Join-Path $workspace 'Unity3D/Arkya' }
$SourceProject = (Resolve-Path -LiteralPath $SourceProject).Path
$ServerExecutable = (Resolve-Path -LiteralPath $ServerExecutable).Path
if (!(Test-Path -LiteralPath $ServerExecutable -PathType Leaf)) { throw 'The dedicated executable is missing.' }
$PythonExecutable = (Get-Command $PythonExecutable -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
$null = Get-Command Get-NetTCPConnection -ErrorAction Stop
$catalogPath = Join-Path $SourceProject 'Assets/Arkya/Characters/Resources/ArkyaAreaCatalog.json'
$protocolPath = Join-Path $SourceProject 'Assets/Arkya/Characters/Runtime/Multiplayer/CharacterRemoteComponents.cs'
$catalog = Get-Content -LiteralPath $catalogPath -Raw | ConvertFrom-Json
$protocolSource = Get-Content -LiteralPath $protocolPath -Raw
$versionMatch = [regex]::Match($protocolSource, 'public const int Version\s*=\s*(\d+)\s*;')
$buildMatch = [regex]::Match($protocolSource, 'public const string BuildId\s*=\s*"([^"]+)"\s*;')
if (!$versionMatch.Success -or !$buildMatch.Success) { throw 'The source multiplayer protocol pair could not be read.' }
$protocolVersion = [int]$versionMatch.Groups[1].Value
$buildId = $buildMatch.Groups[1].Value
$bootstrapScene = 'Assets/Arkya/Characters/Scenes/CharacterDedicated.unity'
$areaIds = @('starter-island', 'handoff-valley')
$contentByArea = @{}
foreach ($areaId in $areaIds) {
    $content = @($catalog.areas | Where-Object contentId -CEQ $areaId)
    if ($content.Count -ne 1) { throw "The source Area catalog must contain exactly one $areaId entry." }
    $contentByArea[$areaId] = $content[0]
}
if (!$RunDirectory) {
    $RunDirectory = Join-Path $workspace ('artifacts/dedicated-area-loading/runs/' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
}
$RunDirectory = [IO.Path]::GetFullPath($RunDirectory)
if (Test-Path -LiteralPath $RunDirectory) { throw 'Choose a new run directory; previous evidence is preserved.' }
$null = New-Item -ItemType Directory -Path $RunDirectory
$privateDirectory = Join-Path $RunDirectory 'private'
$null = New-Item -ItemType Directory -Path $privateDirectory
$acl = Get-Acl -LiteralPath $privateDirectory
$acl.SetAccessRuleProtection($true, $false)
$identity = [Security.Principal.WindowsIdentity]::GetCurrent().User
$rule = [Security.AccessControl.FileSystemAccessRule]::new($identity, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
$acl.SetAccessRule($rule)
Set-Acl -LiteralPath $privateDirectory -AclObject $acl
$configurationPath = Join-Path $privateDirectory 'topology-configuration.json'
$owned = [Collections.Generic.List[object]]::new()
$result = [ordered]@{
    schemaVersion = 1; passed = $false; fixtureKind = 'synthetic-authenticated-topology-only'
    protocolVersion = $protocolVersion; buildId = $buildId; actualDedicatedProcesses = 0
    areas = @(); negativeMismatch = $null; concurrentListeners = @(); failure = $null
    scope = 'Real dedicated processes and engine scene inventories; no client, admission, persistence, handoff, or production deployment validation.'
}

function Write-Json([object]$Value, [string]$Path) {
    ConvertTo-Json -InputObject $Value -Depth 12 | Set-Content -LiteralPath $Path -Encoding utf8NoBOM
}
function Free-GamePorts {
    $reservations = @()
    try {
        for ($index = 0; $index -lt 4; $index++) {
            $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
            $listener.Start(); $reservations += $listener
        }
        return @($reservations | ForEach-Object { ([Net.IPEndPoint]$_.LocalEndpoint).Port })
    }
    finally { foreach ($listener in $reservations) { $listener.Stop() } }
}
function Start-Owned([string]$Name, [string]$Executable, [string[]]$Arguments, [hashtable]$Environment = @{}) {
    $start = [Diagnostics.ProcessStartInfo]::new($Executable)
    $start.WorkingDirectory = Split-Path -Parent $Executable
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    foreach ($key in @($start.Environment.Keys)) {
        if ($key.StartsWith('ARKYA_', [StringComparison]::OrdinalIgnoreCase)) { $null = $start.Environment.Remove($key) }
    }
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    foreach ($key in $Environment.Keys) { $start.Environment[$key] = [string]$Environment[$key] }
    $process = [Diagnostics.Process]::Start($start)
    $record = [PSCustomObject]@{
        name = $Name; id = $process.Id; startedAt = $process.StartTime.ToUniversalTime().ToString('O')
        executable = $Executable; arguments = $Arguments; process = $process
    }
    $owned.Add($record)
    Write-Json @($owned | Select-Object name, id, startedAt, executable, arguments) (Join-Path $RunDirectory 'processes.json')
    return $process
}
function Wait-Ready([string]$Path, [Diagnostics.Process]$Process) {
    $until = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTimeOffset]::UtcNow -lt $until) {
        if ($Process.HasExited) { throw "Owned process $($Process.Id) exited before readiness with code $($Process.ExitCode)." }
        if (Test-Path -LiteralPath $Path -PathType Leaf) { return }
        Start-Sleep -Milliseconds 250
    }
    throw "Owned process $($Process.Id) did not become ready."
}
function Read-Inventories([string]$LogPath) {
    if (!(Test-Path -LiteralPath $LogPath -PathType Leaf)) { return @() }
    $inventories = @()
    $stream = [IO.FileStream]::new($LogPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
    $reader = [IO.StreamReader]::new($stream)
    try {
        while ($null -ne ($line = $reader.ReadLine())) {
            $marker = $line.IndexOf('ARKYA_DEDICATED_SCENES ', [StringComparison]::Ordinal)
            if ($marker -ge 0) {
                $json = $line.Substring($marker + 'ARKYA_DEDICATED_SCENES '.Length)
                if (!$json.TrimEnd().EndsWith('}', [StringComparison]::Ordinal)) { continue }
                $inventories += $json | ConvertFrom-Json
            }
        }
    }
    finally { $reader.Dispose(); $stream.Dispose() }
    return $inventories
}
function Check-Inventories([object]$Server, [object[]]$Inventories, [int]$ProcessId) {
    $expected = @($bootstrapScene, $contentByArea[$Server.areaId].serverScenePath)
    foreach ($inventory in $Inventories) {
        if ($inventory.realmId -cne $realmId -or $inventory.serverId -cne $Server.serverId -or
            $inventory.processId -ne $ProcessId -or
            ($inventory.phase -cne 'bootstrap' -and
                ($inventory.areaId -cne $Server.areaId -or $inventory.contentId -cne $Server.contentId))) {
            throw "Scene inventory identity does not match dedicated $($Server.serverId)."
        }
        $paths = @($inventory.scenePaths)
        if ($paths.Count -eq 0 -or @($paths | Where-Object { $_ -cnotin $expected }).Count -gt 0 -or
            @($paths | Sort-Object -Unique).Count -ne $paths.Count) {
            throw "Dedicated $($Server.serverId) loaded an unexpected scene."
        }
        if ($inventory.phase -cin @('ready', 'steady') -and
            ($paths.Count -ne $expected.Count -or @($expected | Where-Object { $_ -cnotin $paths }).Count -gt 0)) {
            throw "Dedicated $($Server.serverId) did not load exactly its bootstrap and assigned map."
        }
        if ($inventory.phase -ceq 'bootstrap' -and ($paths.Count -ne 1 -or $paths[0] -cne $bootstrapScene)) {
            throw "Dedicated $($Server.serverId) loaded a map before resolving its Area."
        }
    }
}
function Server-Environment([object]$Server, [string]$ReadyPath) {
    return @{
        ARKYA_REALM_ID = $realmId; ARKYA_SERVER_ID = $Server.serverId
        ARKYA_GAME_PORT = ([Uri]$Server.endpoint).Port; ARKYA_WEBSOCKET_PATH = '/'
        ARKYA_CHARACTERS_URL = $origin; ARKYA_DEDICATED_KEY = $Server.key
        ARKYA_ALLOW_LOCAL_HTTP = '1'; ARKYA_READY_FILE = $ReadyPath
        ARKYA_DEDICATED_SCENE_VALIDATION = '1'
        DOTNET_PROCESSOR_COUNT = '4'
    }
}

try {
    $realmId = 'area-loading-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
    $ports = Free-GamePorts
    $servers = @(for ($index = 0; $index -lt 4; $index++) {
        $areaId = $areaIds[[int][math]::Floor($index / 2)]
        [PSCustomObject]@{
            serverId = "island-$areaId-$('{0:D2}' -f ($index % 2 + 1))"
            areaId = $areaId; contentId = $contentByArea[$areaId].contentId
            endpoint = "ws://127.0.0.1:$($ports[$index])/"
            key = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
            bounds = @{ minX = ($index % 2) * 768; maxX = ($index % 2 + 1) * 768; minZ = 0; maxZ = 1536 }
        }
    })
    Write-Json @{ realmId = $realmId; protocolVersion = $protocolVersion; buildId = $buildId; servers = $servers } $configurationPath
    $publicTopology = @{
        realmId = $realmId; protocolVersion = $protocolVersion; buildId = $buildId
        sourceProject = $SourceProject; registrySha256 = (Get-FileHash -LiteralPath $catalogPath).Hash
        protocolSourceSha256 = (Get-FileHash -LiteralPath $protocolPath).Hash
        servers = @($servers | Select-Object serverId, areaId, contentId, endpoint, bounds)
    }
    Write-Json $publicTopology (Join-Path $RunDirectory 'topology.json')
    $runtimeAssembly = Join-Path (Split-Path -Parent $ServerExecutable) ([IO.Path]::GetFileNameWithoutExtension($ServerExecutable) + '_Data/Managed/Arkya.Characters.Runtime.dll')
    $artifacts = @(foreach ($path in @($ServerExecutable, $runtimeAssembly)) {
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            $item = Get-Item -LiteralPath $path
            @{ path = $path; size = $item.Length; sha256 = (Get-FileHash -LiteralPath $path).Hash; lastWriteTime = $item.LastWriteTimeUtc.ToString('O') }
        }
    })
    Write-Json $artifacts (Join-Path $RunDirectory 'build-artifacts.json')
    $fixtureReady = Join-Path $RunDirectory 'topology-fixture.ready.json'
    $eventsPath = Join-Path $RunDirectory 'topology-requests.jsonl'
    $fixture = Start-Owned 'topology-fixture' $PythonExecutable @((Join-Path $PSScriptRoot 'DedicatedAreaTopologyFixture.py'),
        '--configuration', $configurationPath, '--ready', $fixtureReady, '--events', $eventsPath)
    Wait-Ready $fixtureReady $fixture
    $origin = (Get-Content -LiteralPath $fixtureReady -Raw | ConvertFrom-Json).origin
    if (![Uri]::new($origin).IsLoopback) { throw 'The fixture must bind loopback only.' }
    $selectors = @('StarterIsland', 'starter-island', 'HandoffValley', 'handoff-valley')
    $active = @()
    for ($index = 0; $index -lt $servers.Count; $index++) {
        $server = $servers[$index]
        $ready = Join-Path $RunDirectory "dedicated-$index.ready"
        $log = Join-Path $RunDirectory "dedicated-$index.log"
        $process = Start-Owned "dedicated-$index" $ServerExecutable @('-batchmode', '-nographics', '-job-worker-count', '4',
            "--area=$($selectors[$index])", '-logFile', $log) (Server-Environment $server $ready)
        $active += [PSCustomObject]@{ server = $server; process = $process; ready = $ready; log = $log; selector = $selectors[$index] }
    }
    foreach ($entry in $active) { Wait-Ready $entry.ready $entry.process }
    $result.actualDedicatedProcesses = $active.Count
    if (@($active.process.Id | Sort-Object -Unique).Count -ne 4) { throw 'Four distinct dedicated PIDs are required.' }
    $observationStarted = [DateTimeOffset]::UtcNow
    $until = $observationStarted.AddSeconds([math]::Max($TimeoutSeconds, $ObservationSeconds))
    do {
        $complete = $true
        foreach ($entry in $active) {
            if ($entry.process.HasExited) { throw "Dedicated $($entry.server.serverId) exited during observation." }
            $inventories = @(Read-Inventories $entry.log)
            Check-Inventories $entry.server $inventories $entry.process.Id
            foreach ($phase in @('bootstrap', 'sceneLoaded', 'ready', 'steady')) {
                if (!@($inventories | Where-Object phase -CEQ $phase).Count) { $complete = $false }
            }
        }
        if ($complete -and ([DateTimeOffset]::UtcNow - $observationStarted).TotalSeconds -ge $ObservationSeconds) { break }
        Start-Sleep -Milliseconds 500
    } while ([DateTimeOffset]::UtcNow -lt $until)
    if (!$complete -or ([DateTimeOffset]::UtcNow - $observationStarted).TotalSeconds -lt $ObservationSeconds) {
        throw 'Each real dedicated must emit bootstrap, sceneLoaded, ready, and steady engine inventories during the observation window.'
    }
    $connections = @(Get-NetTCPConnection -State Listen -LocalPort $ports -ErrorAction Stop)
    $listeners = @(foreach ($entry in $active) {
        $port = ([Uri]$entry.server.endpoint).Port
        $matching = @($connections | Where-Object { $_.LocalPort -eq $port -and $_.OwningProcess -eq $entry.process.Id })
        if (!$matching.Count -or $entry.process.HasExited) { throw "Dedicated $($entry.server.serverId) does not own its listener while all four processes are alive." }
        @{ processId = $entry.process.Id; serverId = $entry.server.serverId; port = $port; observedAt = [DateTimeOffset]::UtcNow.ToString('O') }
    })
    $result.concurrentListeners = $listeners
    $negativeReady = Join-Path $RunDirectory 'wrong-area.ready'
    $negativeLog = Join-Path $RunDirectory 'wrong-area.log'
    $negative = Start-Owned 'wrong-area' $ServerExecutable @('-batchmode', '-nographics', '-job-worker-count', '4',
        '--area=HandoffValley', '-logFile', $negativeLog) (Server-Environment $servers[0] $negativeReady)
    $negativeUntil = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    while (!$negative.HasExited -and [DateTimeOffset]::UtcNow -lt $negativeUntil) { Start-Sleep -Milliseconds 250 }
    $negativeInventories = @(Read-Inventories $negativeLog)
    if (!$negative.HasExited -or $negative.ExitCode -ne 1 -or (Test-Path -LiteralPath $negativeReady) -or
        $negativeInventories.Count -ne 1 -or $negativeInventories[0].phase -cne 'bootstrap' -or
        $negativeInventories[0].processId -ne $negative.Id -or $negativeInventories[0].realmId -cne $realmId -or
        $negativeInventories[0].serverId -cne $servers[0].serverId -or
        @($negativeInventories[0].scenePaths).Count -ne 1 -or $negativeInventories[0].scenePaths[0] -cne $bootstrapScene -or
        !(Select-String -LiteralPath $negativeLog -SimpleMatch "The requested Area does not match the dedicated server's assigned Area: starter-island" -Quiet)) {
        throw 'An incompatible --area must exit 1 with only its bootstrap scene and without map loading or readiness.'
    }
    $result.negativeMismatch = @{ passed = $true; processId = $negative.Id; exitCode = $negative.ExitCode; requestedArea = 'HandoffValley'; assignedArea = 'starter-island'; inventories = $negativeInventories }
    $requests = @(Get-Content -LiteralPath $eventsPath | ForEach-Object { $_ | ConvertFrom-Json })
    foreach ($server in $servers) {
        if (!@($requests | Where-Object { $_.status -eq 200 -and $_.serverId -ceq $server.serverId }).Count) {
            throw "Dedicated $($server.serverId) did not retrieve its authenticated topology."
        }
    }
    $reports = @(foreach ($entry in $active) {
        if ($entry.process.HasExited) { throw 'All four dedicateds must remain alive through the complete test.' }
        $inventories = @(Read-Inventories $entry.log)
        Check-Inventories $entry.server $inventories $entry.process.Id
        $report = @{
            processId = $entry.process.Id; serverId = $entry.server.serverId; areaId = $entry.server.areaId
            selector = $entry.selector; inventories = $inventories; passed = $true
        }
        Write-Json $report (Join-Path $RunDirectory ($entry.server.serverId + '-scenes.json'))
        $report
    })
    $result.areas = $reports
    $result.passed = $true
}
catch { $result.failure = $_.Exception.Message }
finally {
    foreach ($record in @($owned) | Sort-Object startedAt -Descending) {
        try {
            $process = Get-Process -Id $record.id -ErrorAction SilentlyContinue
            if ($process -and $process.StartTime.ToUniversalTime() -eq ([DateTimeOffset]$record.startedAt).UtcDateTime -and
                $process.Path -eq $record.executable) {
                Stop-Process -Id $process.Id
                if (!$process.WaitForExit(5000)) { throw "Owned process $($record.id) did not stop." }
            }
        }
        catch { $result.passed = $false; $result.failure = 'Owned-process cleanup failed: ' + $_.Exception.Message }
    }
    if (Test-Path -LiteralPath $configurationPath) { Remove-Item -LiteralPath $configurationPath }
    Write-Json $result (Join-Path $RunDirectory 'verification.json')
}
Write-Output (Join-Path $RunDirectory 'verification.json')
if (!$result.passed) { throw 'Dedicated Area loading validation failed; inspect the public run evidence.' }
