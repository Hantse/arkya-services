[CmdletBinding()]
param([string]$InstallDirectory, [switch]$Wait)
. (Join-Path $PSScriptRoot 'Agent.Common.ps1')
$installRoot = Get-AgentInstallRoot $InstallDirectory
if (-not (Test-Path -LiteralPath (Join-Path $installRoot 'config/agent.json'))) { throw 'Install-Agent.ps1 must run first.' }
$existing = Get-OwnedAgentProcess $installRoot
if ($null -ne $existing -and -not $Wait) { Write-Output "Agent déjà démarré (PID $($existing.Id))."; return }
$null = Assert-AgentPackage $installRoot
$python = Join-Path $installRoot 'runtime/python/python.exe'
if (-not (Test-Path -LiteralPath $python)) { throw 'Python runtime is missing. Run Install-Agent.ps1 again.' }
$logDirectory = Join-Path $installRoot 'logs'
$null = New-Item -ItemType Directory -Path $logDirectory -Force
$arguments = @('-u', ('"' + (Join-Path $installRoot 'agent.py') + '"'), '--config',
               ('"' + (Join-Path $installRoot 'config/agent.json') + '"'))
$stopRequest = Join-Path $installRoot 'config/stop.request'
# Clear only on this deliberate start. Internal retries must preserve a concurrent stop request.
if (Test-Path -LiteralPath $stopRequest) { Remove-Item -LiteralPath $stopRequest }
$launch = {
    Start-Process -FilePath $python -ArgumentList $arguments -WorkingDirectory $installRoot -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $logDirectory 'launcher.stdout.log') -RedirectStandardError (Join-Path $logDirectory 'launcher.stderr.log')
}
if ($Wait) {
    $exitCode = Watch-OwnedAgent -Launch $launch -ExistingProcess $existing -StopRequestPath $stopRequest `
        -LogPath (Join-Path $logDirectory 'supervisor.log')
    exit $exitCode
}
$process = & $launch
for ($attempt = 0; $attempt -lt 40; $attempt++) {
    Start-Sleep -Milliseconds 250
    if ($process.HasExited) { throw 'Agent startup failed. Inspect logs/agent.log and logs/launcher.stderr.log.' }
    $owned = Get-OwnedAgentProcess $installRoot
    if ($null -ne $owned) {
        $config = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $installRoot 'config/agent.json') | ConvertFrom-Json
        Write-Output "Agent démarré. Démo : http://127.0.0.1:$($config.demoPort)"
        return
    }
}
throw 'Agent did not publish its verified PID before the startup deadline.'
