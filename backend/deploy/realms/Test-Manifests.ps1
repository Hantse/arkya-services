#Requires -Version 7.2
$ErrorActionPreference = 'Stop'
$directory = Join-Path $PSScriptRoot '.local/tests'
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$config = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'cluster.example.json') -Raw | ConvertFrom-Json -AsHashtable
$digest = 'a' * 64
$config.adminImage = "ghcr.io/arkya/admin@sha256:$digest"
$config.realmsImage = "ghcr.io/arkya/realms@sha256:$digest"
$config.deployment.profiles[0].image = "ghcr.io/arkya/dedicated@sha256:$digest"
$baselineJson = $config | ConvertTo-Json -Depth 30
$configPath = Join-Path $directory 'cluster.json'
function Save-Config { $config | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $configPath -Encoding utf8NoBOM }
function Assert-Check([bool] $Condition, [string] $Message) { if (!$Condition) { throw $Message } }
Save-Config
& (Join-Path $PSScriptRoot 'Render-Manifests.ps1') -Configuration $configPath -OutputDirectory $directory | Out-Null
$manifest = Get-Content -LiteralPath (Join-Path $directory 'operations.json') -Raw | ConvertFrom-Json -AsHashtable
$roles = @($manifest.items | Where-Object kind -eq Role)
Assert-Check ($roles.Count -eq 1) 'One namespaced Role must be created for each target.'
Assert-Check ((@($manifest.items | Where-Object kind -in @('ClusterRole','ClusterRoleBinding','Secret'))).Count -eq 0) 'Manifest must not contain cluster-wide roles or secret values.'
foreach ($rule in $roles[0].rules) {
    Assert-Check (($rule.verbs -join ',') -eq 'get,create') 'Deployer may only read and create its supported resource types.'
}
$admin = $manifest.items | Where-Object { $_.kind -eq 'Deployment' -and $_.metadata.name -eq 'arkya-admin' }
$realms = $manifest.items | Where-Object { $_.kind -eq 'Deployment' -and $_.metadata.name -eq 'arkya-realms' }
Assert-Check (!$admin.spec.template.spec.automountServiceAccountToken) 'Admin must not receive a Kubernetes token.'
Assert-Check ($realms.spec.template.spec.serviceAccountName -eq 'arkya-realms') 'Realms must use its scoped service account.'
Assert-Check ($realms.spec.template.spec.containers[0].readinessProbe.httpGet.scheme -eq 'HTTPS') 'API probe must use HTTPS.'
$adminIngress = $manifest.items | Where-Object { $_.kind -eq 'NetworkPolicy' -and $_.metadata.name -eq 'arkya-admin-ingress' }
$gatewaySource = $adminIngress.spec.ingress[0].from[0]
Assert-Check ($gatewaySource.namespaceSelector.matchLabels['kubernetes.io/metadata.name'] -eq $config.gatewayNamespace) 'Admin ingress must select the gateway namespace.'
Assert-Check ($gatewaySource.podSelector.matchLabels['app.kubernetes.io/name'] -eq 'arkya-gateway') 'Admin ingress must match the actual gateway Deployment labels.'
Assert-Check ((@($manifest.items | Where-Object { $_.kind -eq 'Service' -and $_.spec.type -ne 'ClusterIP' })).Count -eq 0) 'Operations services must remain private.'
$apiConfig = ($manifest.items | Where-Object { $_.kind -eq 'ConfigMap' -and $_.metadata.name -eq 'arkya-realms-config' }).data['appsettings.Production.json'] | ConvertFrom-Json
Assert-Check (!$apiConfig.Deployment.enabled) 'Rendering must preserve deployment-disabled mode.'
$originalNamespace = $config.deployment.targets[0].namespace
$config.deployment.targets[0].namespace = 'kube-system'
Save-Config
$rejected = $false
try { & (Join-Path $PSScriptRoot 'Render-Manifests.ps1') -Configuration $configPath -OutputDirectory $directory | Out-Null } catch { $rejected = $true }
Assert-Check $rejected 'A target must never grant deployment rights in kube-system.'
$config.deployment.targets[0].namespace = $originalNamespace
$originalAdminHost = $config.adminHost
foreach ($invalidHost in @('localhost', '127.0.0.1', '*.arkya.online', 'api.arkya.online', 'backoffice.arkya.online:443', 'backoffice..arkya.online')) {
    $config.adminHost = $invalidHost
    Save-Config
    $rejected = $false
    try { & (Join-Path $PSScriptRoot 'Render-Manifests.ps1') -Configuration $configPath -OutputDirectory $directory | Out-Null } catch { $rejected = $true }
    Assert-Check $rejected 'Admin host must follow the gateway exact DNS hostname contract.'
}
$config.adminHost = $originalAdminHost
$config.adminImage = 'ghcr.io/arkya/admin:latest'
Save-Config
$rejected = $false
try { & (Join-Path $PSScriptRoot 'Render-Manifests.ps1') -Configuration $configPath -OutputDirectory $directory | Out-Null } catch { $rejected = $true }
Assert-Check $rejected 'Mutable image tags must be rejected.'

$invalidOptions = @(
    @{ name = 'short target ID'; change = { param($value) $value.deployment.targets[0].id = 'eu' } },
    @{ name = 'numeric leading profile ID'; change = { param($value) $value.deployment.profiles[0].id = '1test' } },
    @{ name = 'long server ID'; change = { param($value) $value.deployment.profiles[0].servers[0].id = 'a' * 32 } },
    @{ name = 'trailing LF in ID'; change = { param($value) $value.deployment.targets[0].id = "eu-test`n" } },
    @{ name = 'reserved namespace prefix'; change = { param($value) $value.deployment.targets[0].namespace = 'kube-custom' } },
    @{ name = 'trailing LF in namespace'; change = { param($value) $value.deployment.targets[0].namespace = "arkya-test`n" } },
    @{ name = 'empty target name'; change = { param($value) $value.deployment.targets[0].displayName = '' } },
    @{ name = 'missing profile name'; change = { param($value) $value.deployment.profiles[0].Remove('displayName') } },
    @{ name = 'long profile name'; change = { param($value) $value.deployment.profiles[0].displayName = 'a' * 81 } },
    @{ name = 'outer whitespace in name'; change = { param($value) $value.deployment.targets[0].displayName = ' Europe ' } },
    @{ name = 'control in name'; change = { param($value) $value.deployment.profiles[0].displayName = "Island`tserver" } },
    @{ name = 'null args'; change = { param($value) $value.deployment.profiles[0].servers[0].args = $null } },
    @{ name = 'scalar args'; change = { param($value) $value.deployment.profiles[0].servers[0].args = '-batchmode' } },
    @{ name = 'too many args'; change = { param($value) $value.deployment.profiles[0].servers[0].args = @('a') * 65 } },
    @{ name = 'null argument'; change = { param($value) $value.deployment.profiles[0].servers[0].args = @($null) } },
    @{ name = 'numeric argument'; change = { param($value) $value.deployment.profiles[0].servers[0].args = @(12) } },
    @{ name = 'long argument'; change = { param($value) $value.deployment.profiles[0].servers[0].args = @(('a' * 513)) } },
    @{ name = 'NUL argument'; change = { param($value) $value.deployment.profiles[0].servers[0].args = @("a`0b") } },
    @{ name = 'invalid environment secret'; change = { param($value) $value.deployment.profiles[0].environmentSecretName = 'InvalidSecret' } },
    @{ name = 'boolean image pull secret'; change = { param($value) $value.deployment.profiles[0].imagePullSecretName = $false } },
    @{ name = 'long image pull secret'; change = { param($value) $value.deployment.profiles[0].imagePullSecretName = 'a' * 254 } },
    @{ name = 'trailing LF image'; change = { param($value) $value.deployment.profiles[0].image += "`n" } },
    @{ name = 'long image'; change = { param($value) $value.deployment.profiles[0].image = ('a' * 450) + '@sha256:' + ('a' * 64) } },
    @{ name = 'HTTP cluster API'; change = { param($value) $value.deployment.apiServer = 'http://kubernetes.default.svc' } },
    @{ name = 'cluster API prefix'; change = { param($value) $value.deployment.apiServer = 'https://kubernetes.default.svc/api' } },
    @{ name = 'cluster API credentials'; change = { param($value) $value.deployment.apiServer = 'https://user:pass@kubernetes.default.svc' } },
    @{ name = 'empty token path'; change = { param($value) $value.deployment.tokenFile = '' } },
    @{ name = 'null CA path'; change = { param($value) $value.deployment.certificateAuthorityFile = $null } },
    @{ name = 'short reconcile interval'; change = { param($value) $value.deployment.reconcileIntervalSeconds = 1 } },
    @{ name = 'long reconcile interval'; change = { param($value) $value.deployment.reconcileIntervalSeconds = 301 } },
    @{ name = 'short deployment timeout'; change = { param($value) $value.deployment.deploymentTimeoutSeconds = 29 } },
    @{ name = 'long deployment timeout'; change = { param($value) $value.deployment.deploymentTimeoutSeconds = 3601 } },
    @{ name = 'fractional deployment timeout'; change = { param($value) $value.deployment.deploymentTimeoutSeconds = 60.5 } },
    @{ name = 'too many targets'; change = { param($value) $value.deployment.targets = @($value.deployment.targets[0]) * 101 } },
    @{ name = 'too many profiles'; change = { param($value) $value.deployment.profiles = @($value.deployment.profiles[0]) * 51 } },
    @{ name = 'empty enabled targets'; change = { param($value) $value.deployment.enabled = $true; $value.deployment.targets = @() } },
    @{ name = 'empty enabled profiles'; change = { param($value) $value.deployment.enabled = $true; $value.deployment.profiles = @() } }
)
foreach ($case in $invalidOptions) {
    $config = $baselineJson | ConvertFrom-Json -AsHashtable
    & $case.change $config
    Save-Config
    $rejected = $false
    try { & (Join-Path $PSScriptRoot 'Render-Manifests.ps1') -Configuration $configPath -OutputDirectory $directory | Out-Null } catch { $rejected = $true }
    Assert-Check $rejected "Renderer accepted runtime-invalid configuration: $($case.name)."
}

# Optional values and bounds must also remain deployable when intentionally selected.
$config = $baselineJson | ConvertFrom-Json -AsHashtable
$config.deployment.profiles[0].environmentSecretName = $null
$config.deployment.profiles[0].imagePullSecretName = ''
$config.deployment.profiles[0].servers[0].Remove('args')
$config.deployment.reconcileIntervalSeconds = 2
$config.deployment.deploymentTimeoutSeconds = 3600
Save-Config
& (Join-Path $PSScriptRoot 'Render-Manifests.ps1') -Configuration $configPath -OutputDirectory $directory | Out-Null
$config.deployment.profiles[0].servers[0].args = @(('a' * 512)) * 64
$config.deployment.profiles[0].displayName = 'a' * 80
$config.deployment.profiles[0].servers[0].id = 'a' * 31
Save-Config
& (Join-Path $PSScriptRoot 'Render-Manifests.ps1') -Configuration $configPath -OutputDirectory $directory | Out-Null
$config.deployment.targets = @()
$config.deployment.profiles = @()
Save-Config
& (Join-Path $PSScriptRoot 'Render-Manifests.ps1') -Configuration $configPath -OutputDirectory $directory | Out-Null

# Leave the documented safe baseline as the fixture for subsequent schema checks.
$config = $baselineJson | ConvertFrom-Json -AsHashtable
Save-Config
& (Join-Path $PSScriptRoot 'Render-Manifests.ps1') -Configuration $configPath -OutputDirectory $directory | Out-Null
Write-Output "Manifest generation, RBAC, TLS, secret references and $($invalidOptions.Count) runtime-option rejection cases passed. No cluster contacted."
