[CmdletBinding()]
param(
    [string]$ExecutablePath,
    [string]$ServerExecutablePath,
    [string]$RunDirectory,
    [string]$OutputRoot,
    [ValidateRange(10, 600)][int]$DurationSeconds = 45,
    [ValidateRange(0, 2000)][int]$PeerDelayMs = 50,
    [ValidateRange(0.05, 10.0)][double]$MaxPresentationGapSeconds = 1.0,
    [ValidateRange(1, 100000)][int]$InitialHealth = 100,
    [ValidateRange(1, 10000)][int]$ImpactDamage = 10,
    [switch]$VisibleClients
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-ReportProperty($Report, [string]$Name) {
    $property = $Report.PSObject.Properties[$Name]
    if ($null -eq $property) { throw "Missing report property '$Name'." }
    return $property.Value
}

function Add-Check([bool]$Passed, [string]$Message) {
    $checks.Add([pscustomobject]@{ Passed = $Passed; Message = $Message })
    if ($Passed) { Write-Host "PASS $Message" } else { Write-Host "FAIL $Message" -ForegroundColor Red }
}

function Get-OwnedProcess($Worker, [string]$ExpectedPath) {
    $recordedPath = $Worker.PSObject.Properties['ExecutablePath']
    if ($null -ne $recordedPath -and -not [string]::IsNullOrWhiteSpace($recordedPath.Value)) {
        $ExpectedPath = $recordedPath.Value
    }
    $workerProcess = Get-Process -Id $Worker.ProcessId -ErrorAction SilentlyContinue
    if ($null -eq $workerProcess) { return $null }
    try {
        if ($workerProcess.HasExited) { return $null }
        if ($workerProcess.Path -ne $ExpectedPath -or
            $workerProcess.StartTime.ToUniversalTime().Ticks -ne $Worker.StartTimeUtcTicks) { return $null }
        return $workerProcess
    } catch {
        if ($workerProcess.HasExited) { return $null }
        throw
    }
}

function Get-RuntimeLogDiagnostics([string]$Path, [string]$WorkerName) {
    $samples = [Collections.Generic.List[object]]::new()
    $errorCount = 0
    $lineNumber = 0
    $exists = Test-Path -LiteralPath $Path -PathType Leaf
    if ($exists) {
        # Match error records and unequivocal error logging frames, not ordinary stack traces.
        $errorPattern = '(?i)^\s*(?:(?:[A-Za-z_]\w*\.)*(?:[A-Za-z_]\w*)?Exception\s*:|Unhandled\s+exception\b|Assertion(?:\s+failed|\s+failure|Exception)\b|Assert\s+failed\b|(?:Fatal\s+)?Error\s*:|Fatal(?:\s+error)?\b|\[(?:error|exception|assertion|fatal)\]|Shader\s+error\b|Burst\s+error\b|Crash!!!|Received\s+signal\s+SIG(?:SEGV|ABRT)|Access\s+violation\b|UnityEngine\.Debug:(?:LogError|LogAssertion)\s*\()'
        $stream = [IO.FileStream]::new($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read,
            [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
        $reader = [IO.StreamReader]::new($stream)
        try {
            while ($null -ne ($line = $reader.ReadLine())) {
                $lineNumber++
                if ($line -match $errorPattern) {
                    $errorCount++
                    if ($samples.Count -lt 8) {
                        $samples.Add([pscustomobject]@{ Line = $lineNumber; Message = $line })
                    }
                }
            }
        } finally {
            $reader.Dispose()
        }
    }
    return [pscustomobject]@{
        Worker = $WorkerName
        Path = $Path
        Exists = $exists
        ErrorRecords = $errorCount
        Samples = $samples.ToArray()
    }
}

$launchedHere = [string]::IsNullOrWhiteSpace($RunDirectory)
$run = $null
$trackedProcesses = [Collections.Generic.List[object]]::new()
$processExits = @()
try {
    if ($launchedHere) {
        $launchParameters = @{
            DurationSeconds = $DurationSeconds
            PeerDelayMs = $PeerDelayMs
            Automated = $true
            HeadlessClients = -not [bool]$VisibleClients
            DuplicateImpacts = $true
        }
        if (-not [string]::IsNullOrWhiteSpace($ExecutablePath)) { $launchParameters.ExecutablePath = $ExecutablePath }
        if (-not [string]::IsNullOrWhiteSpace($ServerExecutablePath)) { $launchParameters.ServerExecutablePath = $ServerExecutablePath }
        if (-not [string]::IsNullOrWhiteSpace($OutputRoot)) { $launchParameters.OutputRoot = $OutputRoot }
        $run = & (Join-Path $PSScriptRoot 'Start-AreaPrototype.ps1') @launchParameters
        $RunDirectory = $run.RunDirectory
        foreach ($worker in $run.Workers) {
            $workerProcess = Get-OwnedProcess $worker $run.ExecutablePath
            if ($null -ne $workerProcess) {
                # Retain a handle while the process is alive so its exit code remains available.
                $null = $workerProcess.Handle
            }
            $trackedProcesses.Add([pscustomobject]@{ Worker = $worker; Process = $workerProcess })
        }
        $deadline = [DateTimeOffset]::FromUnixTimeMilliseconds($run.StartUnixMs).AddSeconds($run.DurationSeconds + 30)
        $nextProgress = [DateTimeOffset]::UtcNow
        $nextRuntimeLogScan = [DateTimeOffset]::UtcNow
        while ($true) {
            $active = @($trackedProcesses | Where-Object { $null -ne $_.Process -and -not $_.Process.HasExited })
            if ($active.Count -eq 0) { break }
            if ([DateTimeOffset]::UtcNow -gt $deadline) { throw "Prototype processes exceeded their deadline. Reports: $RunDirectory" }
            if ([DateTimeOffset]::UtcNow -ge $nextRuntimeLogScan) {
                $liveDiagnostics = @($run.Workers | ForEach-Object {
                    Get-RuntimeLogDiagnostics (Join-Path $RunDirectory ($_.Name + '.log')) $_.Name
                })
                $failedLogs = @($liveDiagnostics | Where-Object { $_.ErrorRecords -gt 0 })
                if ($failedLogs.Count -gt 0) {
                    $failurePath = Join-Path $RunDirectory 'runtime-failure.json'
                    [pscustomobject]@{
                        RunId = $run.RunId
                        DetectedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
                        Reason = 'runtime_error'
                        RuntimeLogs = $liveDiagnostics
                    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $failurePath -Encoding utf8
                    foreach ($failedLog in $failedLogs) {
                        foreach ($sample in $failedLog.Samples) {
                            Write-Host "  $($failedLog.Path):$($sample.Line): $($sample.Message)" -ForegroundColor Red
                        }
                    }
                    throw "Runtime error detected before the prototype deadline. See $failurePath"
                }
                $nextRuntimeLogScan = [DateTimeOffset]::UtcNow.AddSeconds(2)
            }
            if ([DateTimeOffset]::UtcNow -ge $nextProgress) {
                Write-Host "Waiting for $($active.Count) prototype processes; reports: $RunDirectory"
                $nextProgress = [DateTimeOffset]::UtcNow.AddSeconds(10)
            }
            Start-Sleep -Milliseconds 500
        }
        $processExits = @($trackedProcesses | ForEach-Object {
            [pscustomobject]@{
                Worker = $_.Worker.Name
                ProcessId = $_.Worker.ProcessId
                ExitCodeAvailable = $null -ne $_.Process
                ExitCode = if ($null -ne $_.Process) { $_.Process.ExitCode } else { $null }
            }
        })
        $processExits | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $RunDirectory 'process-exits.json') -Encoding utf8
    } else {
        $RunDirectory = (Resolve-Path -LiteralPath $RunDirectory).Path
        $run = Get-Content -LiteralPath (Join-Path $RunDirectory 'run.json') -Raw | ConvertFrom-Json
        $exitReportPath = Join-Path $RunDirectory 'process-exits.json'
        if (Test-Path -LiteralPath $exitReportPath -PathType Leaf) {
            $processExits = @(Get-Content -LiteralPath $exitReportPath -Raw | ConvertFrom-Json)
        }
    }

    $checks = [Collections.Generic.List[object]]::new()
    $logDiagnostics = [Collections.Generic.List[object]]::new()
    foreach ($processExit in $processExits) {
        if ($processExit.ExitCodeAvailable) {
            Add-Check ([int]$processExit.ExitCode -eq 0) "$($processExit.Worker) exited successfully (code $($processExit.ExitCode))"
        }
    }
    $serverReports = @()
    $clientReports = @()
    foreach ($workerName in @('server-0', 'server-1', 'client-1', 'client-2')) {
        $log = Get-RuntimeLogDiagnostics (Join-Path $RunDirectory "$workerName.log") $workerName
        $logDiagnostics.Add($log)
        Add-Check $log.Exists "$workerName produced its runtime log"
        Add-Check ($log.ErrorRecords -eq 0) "$workerName logged no runtime exceptions, assertions or errors"
        if ($log.ErrorRecords -gt 0) {
            foreach ($sample in $log.Samples) { Write-Host "  $($log.Path):$($sample.Line): $($sample.Message)" }
        }
        $reportPath = Join-Path $RunDirectory "$workerName.json"
        if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) { throw "Missing $workerName report: $reportPath" }
        $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
        $reportRunId = $report.PSObject.Properties['RunId']
        if ($null -ne $reportRunId) { Add-Check ($reportRunId.Value -eq $run.RunId) "$workerName belongs to this run" }
        if ($workerName.StartsWith('server-')) { $serverReports += $report } else { $clientReports += $report }
    }

    $canonicalActors = [Collections.Generic.List[object]]::new()
    $totalAppliedImpacts = 0
    $totalDuplicatesIgnored = 0
    $totalRejectedImpacts = 0
    $totalCrossAreaImpacts = 0
    $totalTransfersOut = 0
    $totalTransfersIn = 0
    foreach ($server in $serverReports) {
        $area = [int](Get-ReportProperty $server 'AreaId')
        Add-Check ($area -in @(0, 1)) "Server has expected area ID $area"
        Add-Check ([long](Get-ReportProperty $server 'Tick') -gt 10) "Area $area advanced its simulation"
        Add-Check ([int](Get-ReportProperty $server 'OutstandingTransfers') -eq 0) "Area $area drained its transfers"
        Add-Check ([int](Get-ReportProperty $server 'PendingImpacts') -eq 0) "Area $area drained its reliable impact queue"
        $totalAppliedImpacts += [int](Get-ReportProperty $server 'AppliedImpacts')
        $totalDuplicatesIgnored += [int](Get-ReportProperty $server 'DuplicateImpactsIgnored')
        $totalRejectedImpacts += [int](Get-ReportProperty $server 'RejectedImpacts')
        $totalCrossAreaImpacts += [int](Get-ReportProperty $server 'CrossAreaImpacts')
        $totalTransfersOut += [int](Get-ReportProperty $server 'TransfersOut')
        $totalTransfersIn += [int](Get-ReportProperty $server 'TransfersIn')
        foreach ($actor in @(Get-ReportProperty $server 'Actors')) {
            if ([int](Get-ReportProperty $actor 'OwnerArea') -eq $area -and [int](Get-ReportProperty $actor 'Phase') -eq 0) {
                $canonicalActors.Add($actor)
            }
        }
    }
    Add-Check ($serverReports[0].AreaId -ne $serverReports[1].AreaId) 'Two distinct area workers produced reports'
    Add-Check ($totalTransfersOut -gt 0 -and $totalTransfersIn -gt 0) 'Entities transferred between the two area servers'
    Add-Check ($totalTransfersOut -eq $totalTransfersIn) 'Completed outbound and inbound transfers agree'
    Add-Check ($totalAppliedImpacts -gt 0) 'Authoritative damage was applied'
    Add-Check ($totalCrossAreaImpacts -gt 0) 'At least one impact crossed the area boundary'
    Add-Check ($totalDuplicatesIgnored -gt 0) 'Repeated impact messages were ignored'
    Add-Check ($totalRejectedImpacts -eq 0) 'The controlled scenario rejected no legitimate impacts'
    Add-Check ($canonicalActors.Count -gt 0) 'Authoritative actors remain at the end of the run'

    $actorIds = @($canonicalActors | ForEach-Object { [string](Get-ReportProperty $_ 'Id') })
    Add-Check (@($actorIds | Select-Object -Unique).Count -eq $actorIds.Count) 'Each entity has one authoritative owner'
    $allHitIds = [Collections.Generic.List[string]]::new()
    foreach ($actor in $canonicalActors) {
        $kind = Get-ReportProperty $actor 'Kind'
        if ($kind -eq 'Projectile' -or $kind -eq 3) { continue }
        $actorId = [string](Get-ReportProperty $actor 'Id')
        $hitIds = @((Get-ReportProperty $actor 'AppliedHitIds') | ForEach-Object { [string]$_ })
        $uniqueHitIds = @($hitIds | Select-Object -Unique)
        Add-Check ($hitIds.Count -eq $uniqueHitIds.Count) "Actor $actorId has no repeated committed impact"
        $expectedHealth = [Math]::Max(0, $InitialHealth - $ImpactDamage * $uniqueHitIds.Count)
        Add-Check ([int](Get-ReportProperty $actor 'Health') -eq $expectedHealth) "Actor $actorId health matches its unique impact ledger"
        foreach ($hitId in $hitIds) { $allHitIds.Add($hitId) }
    }
    Add-Check (@($allHitIds | Select-Object -Unique).Count -eq $allHitIds.Count) 'A single-hit projectile cannot damage two targets'
    Add-Check ($allHitIds.Count -eq $totalAppliedImpacts) 'Impact ledgers survived every authority transfer'

    foreach ($client in $clientReports) {
        $clientId = [int](Get-ReportProperty $client 'ClientId')
        $connected = Get-ReportProperty $client 'ConnectedAreas'
        $connectedCount = if ($connected -is [ValueType]) { [int]$connected } else { @($connected | Select-Object -Unique).Count }
        Add-Check ($connectedCount -ge 2) "Client $clientId connected to both area servers"
        Add-Check ([long](Get-ReportProperty $client 'Frames') -gt 10) "Client $clientId presented the simulation"
        Add-Check ([int](Get-ReportProperty $client 'SceneLoads') -eq 1) "Client $clientId kept one scene loaded"
        Add-Check ([int](Get-ReportProperty $client 'ObservedPlayerTransfers') -gt 0) "Client $clientId observed a player transfer"
        Add-Check ([int](Get-ReportProperty $client 'ObservedMonsterTransfers') -gt 0) "Client $clientId observed a monster transfer"
        Add-Check ([int](Get-ReportProperty $client 'ObservedProjectileTransfers') -gt 0) "Client $clientId observed a projectile transfer"
        Add-Check ([int](Get-ReportProperty $client 'DuplicateVisuals') -eq 0) "Client $clientId displayed no duplicate entities"
        $gap = [double](Get-ReportProperty $client 'MaxPresentationGapSeconds')
        Add-Check ($gap -ge 0 -and $gap -le $MaxPresentationGapSeconds) "Client $clientId presentation gap stayed below $MaxPresentationGapSeconds seconds"
    }
    Add-Check ($clientReports[0].ClientId -ne $clientReports[1].ClientId) 'Two distinct client workers produced reports'

    $failures = @($checks | Where-Object { -not $_.Passed })
    $result = [pscustomobject]@{
        RunId = $run.RunId
        RunDirectory = $RunDirectory
        Passed = $failures.Count -eq 0
        PassedChecks = $checks.Count - $failures.Count
        FailedChecks = $failures.Count
        AppliedImpacts = $totalAppliedImpacts
        CrossAreaImpacts = $totalCrossAreaImpacts
        DuplicateImpactsIgnored = $totalDuplicatesIgnored
        RejectedImpacts = $totalRejectedImpacts
        Transfers = $totalTransfersIn
        ProcessExits = $processExits
        RuntimeLogs = $logDiagnostics.ToArray()
        Checks = $checks.ToArray()
    }
    $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $RunDirectory 'verification.json') -Encoding utf8
    if ($failures.Count -gt 0) { throw "$($failures.Count) prototype checks failed. See $(Join-Path $RunDirectory 'verification.json')" }
    Write-Host "All $($checks.Count) checks passed. Reports: $RunDirectory"
    return $result
} finally {
    if ($launchedHere -and $null -ne $run) {
        foreach ($worker in $run.Workers) {
            $workerProcess = Get-OwnedProcess $worker $run.ExecutablePath
            if ($null -ne $workerProcess) {
                Stop-Process -Id $workerProcess.Id -Force -ErrorAction SilentlyContinue
                $null = $workerProcess.WaitForExit(5000)
            }
        }
        if ($trackedProcesses.Count -gt 0) {
            $finalExits = @($trackedProcesses | ForEach-Object {
                $codeAvailable = $null -ne $_.Process -and $_.Process.HasExited
                [pscustomobject]@{
                    Worker = $_.Worker.Name
                    ProcessId = $_.Worker.ProcessId
                    ExitCodeAvailable = $codeAvailable
                    ExitCode = if ($codeAvailable) { $_.Process.ExitCode } else { $null }
                }
            })
            $finalExits | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $RunDirectory 'process-exits.json') -Encoding utf8
        }
    }
    foreach ($tracked in $trackedProcesses) {
        if ($null -ne $tracked.Process) { $tracked.Process.Dispose() }
    }
}
