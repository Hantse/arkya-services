#requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ProjectPath,
    [string]$UnityExecutable = 'D:/Unity/6000.6.6f1/Editor/Unity.exe'
)
$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$ProjectPath = [IO.Path]::GetFullPath($ProjectPath)
if (!$ProjectPath.StartsWith((Join-Path $workspace 'artifacts') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Run the batch tests only in an owned artifact snapshot.'
}
$lockPath = Join-Path $ProjectPath 'Temp/UnityLockfile'
if (Test-Path -LiteralPath $lockPath) {
    try {
        $stream = [IO.File]::Open($lockPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
        $stream.Dispose()
    }
    catch { throw 'The isolated snapshot is already open.' }
}
$output = Split-Path -Parent $ProjectPath
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$log = Join-Path $output "partition-rules-$stamp.log"
$results = Join-Path $output "partition-rules-$stamp.xml"
$start = [Diagnostics.ProcessStartInfo]::new($UnityExecutable)
$start.WorkingDirectory = $ProjectPath; $start.UseShellExecute = $false; $start.CreateNoWindow = $true
$start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
foreach ($argument in @('-batchmode', '-nographics', '-job-worker-count', '4', '-projectPath', $ProjectPath,
    '-buildTarget', 'Win64', '-standaloneBuildSubtarget', 'Player', '-runTests', '-testPlatform', 'EditMode',
    '-testFilter', 'Arkya.Characters.Tests.CharacterHandoffStateTests;Arkya.Characters.Tests.CharacterAuthorityBoundaryTests',
    '-testResults', $results, '-logFile', $log)) { $start.ArgumentList.Add($argument) }
$start.Environment['DOTNET_PROCESSOR_COUNT'] = '4'
$process = [Diagnostics.Process]::Start($start)
@{ id = $process.Id; startedAt = $process.StartTime.ToUniversalTime().ToString('O'); project = $ProjectPath; log = $log; results = $results } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'partition-rules-process.json')
Write-Output "Unity partition rules PID $($process.Id); $log"
while (!$process.WaitForExit(1000)) { }
if (!(Test-Path -LiteralPath $results)) { throw 'The isolated test runner produced no result file; inspect its log.' }
[xml]$document = Get-Content -LiteralPath $results -Raw
$cases = @($document.SelectNodes('//test-case'))
$failed = @($cases | Where-Object { $_.result -ne 'Passed' })
@{ passed = $cases.Count - $failed.Count; failed = $failed.Count; total = $cases.Count; exitCode = $process.ExitCode; results = $results } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'partition-rules-summary.json')
if ($process.ExitCode -ne 0 -or $cases.Count -eq 0 -or $failed.Count -gt 0) { throw 'The latest partition/input rules did not pass every batch test.' }
Write-Output (Join-Path $output 'partition-rules-summary.json')
