[CmdletBinding()]
param([string]$InstallDirectory)
. (Join-Path $PSScriptRoot 'Agent.Common.ps1')
$installRoot = Get-AgentInstallRoot $InstallDirectory
Stop-OwnedAgent $installRoot
Write-Output 'Agent et moteurs CPU arrêtés.'
