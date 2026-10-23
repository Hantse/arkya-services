#requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ClientExecutable,
    [Parameter(Mandatory)][string]$ServerExecutable,
    [string]$EditorClientProject = '',
    [string]$MongodExecutable = '',
    [string]$MongoUri = '',
    [string]$RunDirectory = '',
    [ValidateRange(60, 900)][int]$TimeoutSeconds = 360,
    [switch]$KeepProcesses
)
$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (!$MongodExecutable) { $MongodExecutable = Join-Path $workspace 'artifacts/inference-package-deps/mongod.exe' }
if ($MongoUri) {
    $mongoEndpoint = [Uri]$MongoUri
    if (!$mongoEndpoint.IsLoopback -or $mongoEndpoint.UserInfo -or $mongoEndpoint.Scheme -ne 'mongodb') {
        throw 'An existing test MongoDB must use an unauthenticated loopback endpoint.'
    }
}
$executables = @($ClientExecutable, $ServerExecutable)
if (!$MongoUri) { $executables += $MongodExecutable }
foreach ($executable in $executables) {
    if (!(Test-Path -LiteralPath $executable -PathType Leaf)) { throw "Required executable is missing: $executable" }
}
$ClientExecutable = (Resolve-Path -LiteralPath $ClientExecutable).Path
$ServerExecutable = (Resolve-Path -LiteralPath $ServerExecutable).Path
if ($EditorClientProject) {
    $EditorClientProject = [IO.Path]::GetFullPath($EditorClientProject)
    if (!$EditorClientProject.StartsWith((Join-Path $workspace 'artifacts') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
        !(Test-Path -LiteralPath (Join-Path $EditorClientProject 'Assets') -PathType Container)) {
        throw 'The Editor client must use an owned artifact snapshot.'
    }
    $lockPath = Join-Path $EditorClientProject 'Temp/UnityLockfile'
    if (Test-Path -LiteralPath $lockPath) {
        try { $stream = [IO.File]::Open($lockPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None); $stream.Dispose() }
        catch { throw 'The Editor client snapshot is already open.' }
    }
}
if (!$MongoUri) { $MongodExecutable = (Resolve-Path -LiteralPath $MongodExecutable).Path }
if (!$RunDirectory) {
    $RunDirectory = Join-Path $workspace ('artifacts/starter-island-partitions/runs/' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
}
$RunDirectory = [IO.Path]::GetFullPath($RunDirectory)
if (Test-Path -LiteralPath $RunDirectory) { throw 'Choose a new run directory; existing evidence is preserved.' }
$null = New-Item -ItemType Directory -Path $RunDirectory
function Write-BuildEvidence {
  $buildArtifacts = foreach ($binary in @($ClientExecutable, $ServerExecutable)) {
    $dataDirectory = Join-Path (Split-Path -Parent $binary) ([IO.Path]::GetFileNameWithoutExtension($binary) + '_Data')
    $runtimeAssembly = if ($EditorClientProject -and $binary -eq $ClientExecutable) {
        Join-Path $EditorClientProject 'Library/ScriptAssemblies/Arkya.Characters.Runtime.dll'
    } else { Join-Path $dataDirectory 'Managed/Arkya.Characters.Runtime.dll' }
    foreach ($file in @($binary, $runtimeAssembly)) {
        if (Test-Path -LiteralPath $file -PathType Leaf) {
            $item = Get-Item -LiteralPath $file
            @{ path = $item.FullName; size = $item.Length; lastWriteTime = $item.LastWriteTimeUtc.ToString('O'); sha256 = (Get-FileHash -LiteralPath $file).Hash }
        }
    }
  }
  $buildArtifacts | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $RunDirectory 'build-artifacts.json')
}
Write-BuildEvidence
$privateDirectory = Join-Path $RunDirectory 'private'
$mongoData = Join-Path $RunDirectory 'mongo-data'
$null = New-Item -ItemType Directory -Path $mongoData
$fixtureScript = Join-Path $workspace 'backend/tools/Start-WebBetaFixture.ps1'
$fixtureTool = Join-Path $workspace 'backend/tools/Arkya.WebBeta.Fixture/bin/Debug/net10.0/Arkya.WebBeta.Fixture.dll'
$owned = [Collections.Generic.List[object]]::new()
$fixtureStarted = $false
$result = [ordered]@{ passed = $false; actualDedicatedProcesses = 0; clientProcesses = 0; clientMode = $(if ($EditorClientProject) { 'Editor' } else { 'Native' }); failure = $null }

function Free-Port {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    try { return ([Net.IPEndPoint]$listener.LocalEndpoint).Port }
    finally { $listener.Stop() }
}
function Four-FreePorts {
    for ($candidate = 18143; $candidate -lt 25000; $candidate += 4) {
        $listeners = @()
        try {
            for ($index = 0; $index -lt 4; $index++) {
                $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $candidate + $index)
                $listener.Start(); $listeners += $listener
            }
            return $candidate
        }
        catch { }
        finally { foreach ($listener in $listeners) { $listener.Stop() } }
    }
    throw 'No four consecutive loopback ports are available.'
}
function Start-Owned([string]$Name, [string]$Executable, [string[]]$Arguments, [hashtable]$Environment = @{}, [switch]$Client) {
    $start = [Diagnostics.ProcessStartInfo]::new($Executable)
    $start.WorkingDirectory = Split-Path -Parent $Executable
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    if ($Client) {
        foreach ($key in @($start.Environment.Keys)) {
            if ($key -match 'ARKYA_.*(KEY|SECRET|TICKET|MONGO)|ARKYA_TEST_MONGO') { $null = $start.Environment.Remove($key) }
        }
    }
    foreach ($key in $Environment.Keys) { $start.Environment[$key] = [string]$Environment[$key] }
    $process = [Diagnostics.Process]::Start($start)
    $record = [PSCustomObject]@{ name = $Name; id = $process.Id; startedAt = $process.StartTime.ToUniversalTime().ToString('O'); executable = $Executable; process = $process }
    $owned.Add($record)
    @($owned | Select-Object name, id, startedAt, executable) | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $RunDirectory 'processes.json')
    return $process
}
function Fixture-Action([string]$Action) {
    $output = & dotnet $fixtureTool $Action $privateDirectory
    if ($LASTEXITCODE -ne 0) { throw "Isolated fixture action failed: $Action" }
    return $output
}
function Wait-File([string]$Path, [Diagnostics.Process]$Process, [int]$Seconds) {
    $until = [DateTimeOffset]::UtcNow.AddSeconds($Seconds)
    while ([DateTimeOffset]::UtcNow -lt $until) {
        if ($Process.HasExited) { throw 'A dedicated process exited before readiness.' }
        if (Test-Path -LiteralPath $Path) { return }
        Start-Sleep -Milliseconds 250
    }
    throw 'A dedicated process did not become ready.'
}
function Copy-ClientEvidence {
    foreach ($entry in @(
        @('client-report.json', 'client-report.json'),
        @('client-report.startup.json', 'client-startup.json'),
        @('client-report.json.startup.json', 'client-startup.json'),
        @('client-report.crossing.png', 'client-crossing.png'),
        @('client-report.completed.png', 'client-completed.png'))) {
        $sourcePath = Join-Path $privateDirectory $entry[0]
        if (Test-Path -LiteralPath $sourcePath) {
            Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $RunDirectory $entry[1]) -Force
        }
    }
}
try {
    $gamePort = Four-FreePorts
    if (!$MongoUri) {
        $mongoPort = Free-Port
        $MongoUri = "mongodb://127.0.0.1:$mongoPort"
        $mongoProcess = Start-Owned 'mongo' $MongodExecutable @('--bind_ip', '127.0.0.1', '--port', [string]$mongoPort,
            '--dbpath', $mongoData, '--logpath', (Join-Path $RunDirectory 'mongo.log'), '--logappend', '--quiet')
    }
    $previousMongo = $env:ARKYA_TEST_MONGO; $previousEndpoint = $env:ARKYA_TEST_GAME_ENDPOINT
    try {
        $env:ARKYA_TEST_MONGO = $MongoUri
        $env:ARKYA_TEST_GAME_ENDPOINT = "ws://127.0.0.1:$gamePort/"
        $fixtureStarted = $true
        & $fixtureScript -Action Start -Directory $privateDirectory -ServerCount 4 | Out-Null
    }
    finally { $env:ARKYA_TEST_MONGO = $previousMongo; $env:ARKYA_TEST_GAME_ENDPOINT = $previousEndpoint }
    $fixture = Get-Content -LiteralPath (Join-Path $privateDirectory 'fixture.json') -Raw | ConvertFrom-Json
    if ($fixture.servers.Count -ne 4) { throw 'The fixture must configure exactly four dedicated authorities.' }
    $null = Fixture-Action 'seed-partition-state'
    foreach ($server in $fixture.servers) {
        $index = [int]$server.partitionIndex
        $ready = Join-Path $RunDirectory "dedicated-$index.ready"
        $environment = @{
            ARKYA_REALM_ID = $fixture.realmId; ARKYA_SERVER_ID = $server.serverId
            ARKYA_GAME_PORT = ([Uri]$server.endpoint).Port; ARKYA_WEBSOCKET_PATH = ([Uri]$server.endpoint).AbsolutePath
            ARKYA_CHARACTERS_URL = $fixture.charactersBaseUrl; ARKYA_DEDICATED_KEY = $server.dedicatedKey
            ARKYA_ALLOW_LOCAL_HTTP = '1'; ARKYA_READY_FILE = $ready
            ARKYA_PARTITIONS_FILE = (Join-Path $privateDirectory 'partitions.json')
        }
        $process = Start-Owned "dedicated-$index" $ServerExecutable @('-batchmode', '-nographics', '-logFile', (Join-Path $RunDirectory "dedicated-$index.log")) $environment
        Wait-File $ready $process 90
        $result.actualDedicatedProcesses++
    }
    $null = Fixture-Action 'tickets'
    $null = Fixture-Action 'client-config'
    $clientArguments = if ($EditorClientProject) {
        @('-batchmode', '-job-worker-count', '4', '-buildTarget', 'Win64', '-standaloneBuildSubtarget', 'Player',
            '-projectPath', $EditorClientProject, '-executeMethod', 'Arkya.Characters.Editor.CharacterPartitionEditorValidation.Run',
            '-logFile', (Join-Path $RunDirectory 'client.log'))
    } else {
        @('-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720', '-logFile', (Join-Path $RunDirectory 'client.log'))
    }
    $clientName = if ($EditorClientProject) { 'editor-client' } else { 'native-client' }
    $client = Start-Owned $clientName $ClientExecutable $clientArguments @{ ARKYA_PARTITION_VALIDATION_FILE = (Join-Path $privateDirectory 'client.json'); DOTNET_PROCESSOR_COUNT = '4' } -Client
    $result.clientProcesses = 1
    $until = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    while (!$client.HasExited -and [DateTimeOffset]::UtcNow -lt $until) {
        $observation = (Fixture-Action 'partition-state') -join "`n" | ConvertFrom-Json
        $observation | ConvertTo-Json -Compress | Add-Content -LiteralPath (Join-Path $RunDirectory 'lease-observations.jsonl')
        Start-Sleep -Milliseconds 500
    }
    if (!$client.HasExited) { throw 'Client traversal timed out.' }
    if ($client.ExitCode -ne 0) { throw 'Client reported a failed traversal.' }
    $clientReport = Get-Content -LiteralPath (Join-Path $privateDirectory 'client-report.json') -Raw | ConvertFrom-Json
    if ($clientReport.outcome -ne 'Passed' -or $clientReport.visitedServers.Count -ne 4 -or $clientReport.completedHandoffs -lt 4) {
        throw 'The native client report does not confirm four-authority traversal.'
    }
    Copy-ClientEvidence
    Fixture-Action 'verify-partition-state' | Set-Content -LiteralPath (Join-Path $RunDirectory 'persisted-state.json')
    Fixture-Action 'verify-partition-relogin' | Set-Content -LiteralPath (Join-Path $RunDirectory 'relogin-state.json')
    foreach ($server in $owned | Where-Object name -like 'dedicated-*') {
        if ($server.process.HasExited) { throw 'A dedicated exited during the traversal.' }
    }
    $result.passed = $true
}
catch {
    $result.failure = $_.Exception.Message
}
finally {
    Write-BuildEvidence
    Copy-ClientEvidence
    $result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $RunDirectory 'verification.json')
    if (!$KeepProcesses) {
        foreach ($record in @($owned | Where-Object name -ne 'mongo') | Sort-Object startedAt -Descending) {
            $process = Get-Process -Id $record.id -ErrorAction SilentlyContinue
            if ($process -and $process.StartTime.ToUniversalTime() -eq ([DateTimeOffset]$record.startedAt).UtcDateTime -and $process.Path -eq $record.executable) {
                Stop-Process -Id $process.Id; $null = $process.WaitForExit(5000)
            }
        }
        if ($fixtureStarted -and (Test-Path -LiteralPath (Join-Path $privateDirectory 'fixture.json'))) {
            & $fixtureScript -Action Stop -Directory $privateDirectory | Out-Null
        }
        foreach ($record in $owned | Where-Object name -eq 'mongo') {
            $process = Get-Process -Id $record.id -ErrorAction SilentlyContinue
            if ($process -and $process.StartTime.ToUniversalTime() -eq ([DateTimeOffset]$record.startedAt).UtcDateTime -and $process.Path -eq $record.executable) {
                Stop-Process -Id $process.Id; $null = $process.WaitForExit(5000)
            }
        }
    }
}
Write-Output (Join-Path $RunDirectory 'verification.json')
if (!$result.passed) { throw 'The four-process StarterIsland validation failed; inspect the run evidence.' }
