#Requires -Version 7.2
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$testRoot = Join-Path $repositoryRoot 'output/gateway-admin-rendering'
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'base') -Destination $testRoot -Recurse -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Render-Manifests.ps1') -Destination $testRoot -Force
$renderer = Join-Path $testRoot 'Render-Manifests.ps1'
$configurationPath = Join-Path $testRoot 'cluster.json'
$baseline = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'cluster.example.json') -Raw | ConvertFrom-Json -AsHashtable
$baseline.image = 'ghcr.io/arkya-qa/arkya-gateway@sha256:' + ('a' * 64)

function Copy-Configuration($value) { $value | ConvertTo-Json -Depth 10 | ConvertFrom-Json -AsHashtable }
function Render-Case($value, $name) {
    $value | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $configurationPath
    & $renderer -Configuration $configurationPath | Out-Null
    $manifest = Get-Content -LiteralPath (Join-Path $testRoot '.local/production/gateway.yaml') -Raw
    $manifest | Set-Content -LiteralPath (Join-Path $testRoot "$name.yaml")
    return $manifest
}
function Assert-Value([string] $manifest, [string] $key, [string] $value) {
    $pattern = '(?m)^  ' + [regex]::Escape($key) + ': "?' + [regex]::Escape($value) + '"?\r?$'
    if ($manifest -notmatch $pattern) { throw "Expected ConfigMap $key=$value." }
}

$disabled = Render-Case $baseline 'disabled'
Assert-Value $disabled 'Gateway__AdminEnabled' 'false'
Assert-Value $disabled 'AllowedHosts' 'api.arkya.online'
if ($disabled -match 'Gateway__AdminAddress:|Gateway__AdminHost:') { throw 'Disabled backoffice leaked its destination or host.' }

$enabled = Copy-Configuration $baseline
$enabled.adminAddress = 'http://arkya-admin.arkya-platform.svc.cluster.local:8080/'
$manifest = Render-Case $enabled 'enabled'
Assert-Value $manifest 'Gateway__AdminEnabled' 'true'
Assert-Value $manifest 'Gateway__AdminHost' 'backoffice.arkya.online'
Assert-Value $manifest 'Gateway__AdminAddress' $enabled.adminAddress
Assert-Value $manifest 'AllowedHosts' 'api.arkya.online;backoffice.arkya.online'

$custom = Copy-Configuration $enabled
$custom.adminHost = 'operations.arkya.online'
$custom.adminAddress = 'https://arkya-admin.arkya-platform.svc.cluster.local:8443/'
$manifest = Render-Case $custom 'custom-https'
Assert-Value $manifest 'Gateway__AdminHost' $custom.adminHost
Assert-Value $manifest 'Gateway__AdminAddress' $custom.adminAddress
Assert-Value $manifest 'AllowedHosts' 'api.arkya.online;operations.arkya.online'
if ((Render-Case $baseline 'reset') -ne $disabled) { throw 'Disabling backoffice did not restore the original manifest.' }

$invalidCases = @(
    @{ key = 'adminHost'; value = 'api.arkya.online' },
    @{ key = 'adminHost'; value = 'API.ARKYA.ONLINE' },
    @{ key = 'adminHost'; value = '*.arkya.online' },
    @{ key = 'adminHost'; value = 'https://backoffice.arkya.online' },
    @{ key = 'adminHost'; value = 'backoffice.arkya.online:443' },
    @{ key = 'adminHost'; value = 'backoffice.arkya.online.' },
    @{ key = 'adminHost'; value = '-backoffice.arkya.online' },
    @{ key = 'adminHost'; value = '127.0.0.1' },
    @{ key = 'adminHost'; value = 'localhost' },
    @{ key = 'adminHost'; value = '' },
    @{ key = 'adminHost'; value = 42 },
    @{ key = 'adminAddress'; value = 42 },
    @{ key = 'adminAddress'; value = 'http://user:password@arkya-admin.internal/' },
    @{ key = 'adminAddress'; value = 'http://arkya-admin.internal/prefix/' },
    @{ key = 'adminAddress'; value = 'http://arkya-admin.internal/?query=1' },
    @{ key = 'adminAddress'; value = 'http://arkya-admin.internal/#fragment' },
    @{ key = 'adminAddress'; value = 'http://arkya-admin.internal\' },
    @{ key = 'adminAddress'; value = ' http://arkya-admin.internal/' },
    @{ key = 'adminAddress'; value = 'ftp://arkya-admin.internal/' }
)
foreach ($case in $invalidCases) {
    $invalid = Copy-Configuration $enabled
    $invalid[$case.key] = $case.value
    $invalid | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $configurationPath
    $rejected = $false
    try { & $renderer -Configuration $configurationPath | Out-Null } catch { $rejected = $true }
    if (!$rejected) { throw "Renderer accepted invalid $($case.key)." }
}
Write-Output "Four backoffice rendering scenarios passed; $($invalidCases.Count) invalid configurations rejected. No cluster access."
Write-Output "Review artifacts: $testRoot"
