[CmdletBinding()]
param([string]$InstallDirectory, [switch]$RemoveModelCache)
. (Join-Path $PSScriptRoot 'Agent.Common.ps1')
$installRoot = [IO.Path]::GetFullPath((Get-AgentInstallRoot $InstallDirectory)).TrimEnd('\')
$configPath = Join-Path $installRoot 'config/agent.json'
if (-not (Test-Path -LiteralPath $configPath)) { throw 'No Arkya inference installation exists at this exact path.' }
if ($installRoot -eq [IO.Path]::GetPathRoot($installRoot).TrimEnd('\')) { throw 'Refusing filesystem root.' }
Assert-AgentNoReparsePoints $installRoot
$config = Get-Content -Raw -Encoding UTF8 -LiteralPath $configPath | ConvertFrom-Json
$expectedExecutable = [IO.Path]::GetFullPath((Join-Path $installRoot 'runtime/ollama/ollama.exe'))
if ($config.ollamaExecutable -ne $expectedExecutable) { throw 'Installation identity does not match its directory.' }
Stop-OwnedAgent $installRoot
$installation = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $installRoot 'config/installation.json') | ConvertFrom-Json
if ($installation.installRoot -ne $installRoot) { throw 'Installation metadata does not match its directory.' }
if ($installation.startupMode -ne 'manual') {
    $task = Get-ScheduledTask -TaskName $installation.taskName -ErrorAction SilentlyContinue
    if ($task) {
        if (-not $task.Actions.Arguments.Contains((Join-Path $installRoot 'Start-Agent.ps1'))) { throw 'Task ownership mismatch.' }
        Unregister-ScheduledTask -TaskName $installation.taskName -Confirm:$false
    }
}
$modelRoot = [IO.Path]::GetFullPath($config.modelCache).TrimEnd('\')
if ($RemoveModelCache -and (Test-Path -LiteralPath $modelRoot)) {
    Assert-AgentNoReparsePoints $modelRoot
    if ($modelRoot -eq [IO.Path]::GetPathRoot($modelRoot).TrimEnd('\') -or $modelRoot -eq $installRoot -or
        -not (Test-Path -LiteralPath (Join-Path $modelRoot 'blobs'))) { throw 'Model cache identity check failed; nothing recursively removed.' }
    # This is the exact modelCache explicitly recorded by this installation.
    Remove-Item -LiteralPath $modelRoot -Recurse -Force
}
# Remove only our manifest-listed files and our named state/runtime directories; preserve other files.
$manifest = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $installRoot 'package-manifest.json') | ConvertFrom-Json
$verifiedRoot = $installRoot + '\'
foreach ($file in $manifest.files) {
    $target = [IO.Path]::GetFullPath((Join-Path $installRoot $file.path))
    if (-not $target.StartsWith($verifiedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid manifest deletion target.' }
    if (Test-Path -LiteralPath $target -PathType Leaf) { Remove-Item -LiteralPath $target -Force }
}
foreach ($name in @('config','logs','runtime','downloads')) {
    $target = [IO.Path]::GetFullPath((Join-Path $installRoot $name))
    if (-not $target.StartsWith($verifiedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid deletion target.' }
    if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
}
Remove-Item -LiteralPath (Join-Path $installRoot 'package-manifest.json') -Force
Write-Output "Agent désinstallé. Cache modèle conservé sauf -RemoveModelCache : $modelRoot"
