#Requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Configuration,
    [string] $OutputDirectory = (Join-Path $PSScriptRoot '.local')
)
$ErrorActionPreference = 'Stop'
$config = Get-Content -LiteralPath $Configuration -Raw | ConvertFrom-Json -AsHashtable
function Assert-Name($Value, [string] $Field) {
    if ($Value -isnot [string] -or $Value -cnotmatch '\A[a-z0-9]([-a-z0-9]*[a-z0-9])?\z' -or $Value.Length -gt 63) {
        throw "$Field must be a Kubernetes DNS label."
    }
}
function Assert-Image($Value) {
    if ($Value -isnot [string] -or $Value.Length -gt 512 -or $Value -cnotmatch '\A[a-z0-9][a-z0-9./:_-]*@sha256:[a-f0-9]{64}\z') {
        throw 'Images must be immutable registry references with a sha256 digest.'
    }
}
function Assert-Id($Value, [string] $Field) {
    if ($Value -isnot [string] -or $Value -cnotmatch '\A[a-z][a-z0-9-]{1,29}[a-z0-9]\z') {
        throw "$Field must contain 3 to 31 lowercase letters, digits or hyphens, beginning with a letter and ending with a letter or digit."
    }
}
function Assert-DisplayName($Value, [string] $Field) {
    if ($Value -isnot [string] -or $Value.Length -lt 1 -or $Value.Length -gt 80 -or $Value -ne $Value.Trim() -or $Value -match '\p{Cc}') {
        throw "$Field must contain 1 to 80 characters, without outer whitespace or control characters."
    }
}
function Assert-OptionalSecret($Value, [string] $Field) {
    if ($null -eq $Value -or ($Value -is [string] -and $Value.Length -eq 0)) { return }
    if ($Value -isnot [string] -or $Value.Length -gt 253 -or $Value -cnotmatch '\A[a-z0-9]([a-z0-9.-]*[a-z0-9])?\z') {
        throw "$Field must be an optional Kubernetes Secret name."
    }
}
function Assert-IntegerRange($Value, [int] $Minimum, [int] $Maximum, [string] $Field) {
    if (($Value -isnot [long] -and $Value -isnot [int]) -or $Value -lt $Minimum -or $Value -gt $Maximum) {
        throw "$Field must be an integer between $Minimum and $Maximum."
    }
}
Assert-Name $config.operationsNamespace 'operationsNamespace'
Assert-Name $config.gatewayNamespace 'gatewayNamespace'
if ($config.operationsNamespace -eq $config.gatewayNamespace -or $config.operationsNamespace -eq 'default' -or $config.operationsNamespace.StartsWith('kube-')) {
    throw 'Use a dedicated operations namespace, separate from the gateway and Kubernetes system namespaces.'
}
$adminHostIp = $null
if ($config.adminHost -isnot [string] -or $config.adminHost.Length -gt 253 -or
    $config.adminHost -cnotmatch '\A[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)+\z' -or
    [Net.IPAddress]::TryParse($config.adminHost, [ref]$adminHostIp) -or $config.adminHost -ieq 'api.arkya.online') {
    throw 'adminHost must be a distinct, explicit DNS hostname.'
}
Assert-Image $config.adminImage
Assert-Image $config.realmsImage
Assert-OptionalSecret $config.imagePullSecretName 'imagePullSecretName'
if ([string]::IsNullOrWhiteSpace($config.storageClassName)) { throw 'Set storageClassName for persistent administration cookies.' }
if ($config.trustedGatewayPodNetworks -isnot [array] -or !$config.trustedGatewayPodNetworks.Count) { throw 'Provide actual gateway pod CIDRs.' }
foreach ($cidr in $config.trustedGatewayPodNetworks) {
    [Net.IPNetwork] $network = [Net.IPNetwork]::Parse('127.0.0.0/8')
    if ($cidr -isnot [string] -or $cidr -ne $cidr.Trim() -or ![Net.IPNetwork]::TryParse($cidr, [ref]$network) -or $network.PrefixLength -eq 0 -or
        ($network.BaseAddress.AddressFamily -eq [Net.Sockets.AddressFamily]::InterNetworkV6 -and $network.PrefixLength -le 96 -and $network.Contains([Net.IPAddress]::Parse('::ffff:0.0.0.0')))) {
        throw 'Gateway trust requires restricted, valid CIDRs.'
    }
}
if ($config.deployment -isnot [Collections.IDictionary] -or $config.deployment.enabled -isnot [bool]) { throw 'deployment.enabled must be a boolean.' }
if ($config.deployment.targets -isnot [array] -or $config.deployment.targets.Count -gt 100 -or
    $config.deployment.profiles -isnot [array] -or $config.deployment.profiles.Count -gt 50 -or
    ($config.deployment.enabled -and (!$config.deployment.targets.Count -or !$config.deployment.profiles.Count))) {
    throw 'Configure at most 100 targets and 50 profiles; enabled deployment requires at least one of each.'
}
$apiServer = if ($config.deployment.ContainsKey('apiServer')) { $config.deployment.apiServer } else { 'https://kubernetes.default.svc' }
$apiUri = $null
if ($apiServer -isnot [string] -or ![Uri]::TryCreate($apiServer, [UriKind]::Absolute, [ref]$apiUri) -or
    $apiUri.Scheme -ne 'https' -or $apiUri.UserInfo -or $apiUri.Query -or $apiUri.Fragment -or $apiUri.AbsolutePath -ne '/') {
    throw 'deployment.apiServer must be an HTTPS origin without credentials, query, fragment or path prefix.'
}
foreach ($field in @('tokenFile','certificateAuthorityFile')) {
    if ($config.deployment.ContainsKey($field) -and ($config.deployment[$field] -isnot [string] -or [string]::IsNullOrWhiteSpace($config.deployment[$field]))) {
        throw "deployment.$field must be a nonempty file path."
    }
}
if ($config.deployment.ContainsKey('reconcileIntervalSeconds')) { Assert-IntegerRange $config.deployment.reconcileIntervalSeconds 2 300 'deployment.reconcileIntervalSeconds' }
if ($config.deployment.ContainsKey('deploymentTimeoutSeconds')) { Assert-IntegerRange $config.deployment.deploymentTimeoutSeconds 30 3600 'deployment.deploymentTimeoutSeconds' }
$targetIds = @{}; $namespaces = @{}; $profileIds = @{}
foreach ($target in $config.deployment.targets) {
    Assert-Id $target.id 'target.id'; Assert-Name $target.namespace 'target.namespace'
    Assert-DisplayName $target.displayName 'target.displayName'
    if ($targetIds.ContainsKey($target.id) -or $namespaces.ContainsKey($target.namespace)) { throw 'Targets and target namespaces must be unique.' }
    if ($target.namespace -in @($config.operationsNamespace, $config.gatewayNamespace, 'default') -or $target.namespace.StartsWith('kube-')) { throw 'Each target must use a dedicated realm namespace.' }
    $targetIds[$target.id] = $true; $namespaces[$target.namespace] = $true
}
foreach ($profile in $config.deployment.profiles) {
    Assert-Id $profile.id 'profile.id'; Assert-Image $profile.image
    Assert-DisplayName $profile.displayName 'profile.displayName'
    Assert-OptionalSecret $profile.environmentSecretName 'profile.environmentSecretName'
    Assert-OptionalSecret $profile.imagePullSecretName 'profile.imagePullSecretName'
    if ($profileIds.ContainsKey($profile.id)) { throw 'Profile IDs must be unique.' }
    $profileIds[$profile.id] = $true
    if ($profile.servers -isnot [array] -or !$profile.servers.Count -or $profile.servers.Count -gt 16) { throw 'A profile requires 1 to 16 named servers.' }
    $serverIds = @{}; $ports = @{}
    foreach ($server in $profile.servers) {
        Assert-Id $server.id 'server.id'
        Assert-IntegerRange $server.port 1024 65535 'server.port'
        if ($ports.ContainsKey($server.port) -or $serverIds.ContainsKey($server.id)) { throw 'Server IDs and unprivileged UDP ports must be unique within a profile.' }
        if ($server.ContainsKey('args')) {
            if ($server.args -isnot [array] -or $server.args.Count -gt 64) { throw 'server.args must be an array of at most 64 strings.' }
            foreach ($argument in $server.args) {
                if ($argument -isnot [string] -or $argument.Length -gt 512 -or $argument.Contains([char]0)) {
                    throw 'Each server argument must contain at most 512 characters and no NUL character.'
                }
            }
        }
        $ports[$server.port] = $true; $serverIds[$server.id] = $true
    }
}
$ops = $config.operationsNamespace
$appHost = "arkya-realms.$ops.svc"
$items = [Collections.Generic.List[object]]::new()
function Add-Resource([string] $Api, [string] $Kind, [string] $Name, [string] $Namespace, [hashtable] $Extra) {
    $meta = @{ name = $Name }
    if ($Namespace) { $meta.namespace = $Namespace }
    $item = @{ apiVersion = $Api; kind = $Kind; metadata = $meta }
    foreach ($key in $Extra.Keys) { $item[$key] = $Extra[$key] }
    $items.Add($item)
}
Add-Resource v1 Namespace $ops '' @{}
Add-Resource v1 ServiceAccount arkya-realms $ops @{}
Add-Resource v1 ConfigMap arkya-realms-config $ops @{ data = @{
    'appsettings.Production.json' = (@{
        AllowedHosts = "$appHost;$appHost.cluster.local;localhost"
        Mongo = @{ DatabaseName = 'arkya_realms' }
        Deployment = $config.deployment
        Kestrel = @{ Endpoints = @{ Https = @{ Url = 'https://0.0.0.0:8443'; Certificate = @{ Path = '/tls/tls.crt'; KeyPath = '/tls/tls.key' } } } }
    } | ConvertTo-Json -Depth 30)
} }
Add-Resource v1 ConfigMap arkya-admin-config $ops @{ data = @{
    'appsettings.Production.json' = (@{
        AllowedHosts = $config.adminHost
        ContentApi = @{ Enabled = $false }
        RealmsApi = @{ Enabled = $true; BaseUrl = "https://${appHost}:8443"; CertificateAuthorityFile = '/realm-ca/ca.crt' }
        DataProtection = @{ KeysPath = '/var/lib/arkya/keys' }
        ProxyTrust = @{ KnownNetworks = $config.trustedGatewayPodNetworks; ForwardLimit = 1 }
    } | ConvertTo-Json -Depth 10)
} }
Add-Resource v1 PersistentVolumeClaim arkya-admin-keys $ops @{ spec = @{
    accessModes = @('ReadWriteOnce'); storageClassName = $config.storageClassName; resources = @{ requests = @{ storage = '1Gi' } }
} }
foreach ($app in @('admin', 'realms')) {
    $isAdmin = $app -eq 'admin'; $name = "arkya-$app"; $port = if ($isAdmin) { 8080 } else { 8443 }
    $env = @(@{ name = 'ASPNETCORE_ENVIRONMENT'; value = 'Production' })
    if ($isAdmin) {
        $env += @{ name = 'Admin__Password'; valueFrom = @{ secretKeyRef = @{ name = 'arkya-operations-secrets'; key = 'admin-password' } } }
        $env += @{ name = 'RealmsApi__AdminKey'; valueFrom = @{ secretKeyRef = @{ name = 'arkya-operations-secrets'; key = 'realms-admin-key' } } }
    } else {
        $env += @{ name = 'RealmsAdmin__Key'; valueFrom = @{ secretKeyRef = @{ name = 'arkya-operations-secrets'; key = 'realms-admin-key' } } }
        $env += @{ name = 'Mongo__ConnectionString'; valueFrom = @{ secretKeyRef = @{ name = 'arkya-realms-mongo'; key = 'connection-string' } } }
    }
    $mounts = @(@{ name = 'config'; mountPath = '/app/appsettings.Production.json'; subPath = 'appsettings.Production.json'; readOnly = $true }, @{ name = 'tmp'; mountPath = '/tmp' })
    $volumes = @(@{ name = 'config'; configMap = @{ name = "$name-config" } }, @{ name = 'tmp'; emptyDir = @{} })
    if ($isAdmin) {
        $mounts += @{ name = 'keys'; mountPath = '/var/lib/arkya/keys' }
        $mounts += @{ name = 'realm-ca'; mountPath = '/realm-ca'; readOnly = $true }
        $volumes += @{ name = 'keys'; persistentVolumeClaim = @{ claimName = 'arkya-admin-keys' } }
        $volumes += @{ name = 'realm-ca'; secret = @{ secretName = 'arkya-realms-tls'; items = @(@{ key = 'ca.crt'; path = 'ca.crt' }) } }
    } else {
        $mounts += @{ name = 'tls'; mountPath = '/tls'; readOnly = $true }
        $volumes += @{ name = 'tls'; secret = @{ secretName = 'arkya-realms-tls'; items = @(@{ key = 'tls.crt'; path = 'tls.crt' }, @{ key = 'tls.key'; path = 'tls.key' }) } }
    }
    $container = @{
        name = $app; image = $(if ($isAdmin) { $config.adminImage } else { $config.realmsImage })
        ports = @(@{ name = 'web'; containerPort = $port }); env = $env; volumeMounts = $mounts
        securityContext = @{ allowPrivilegeEscalation = $false; readOnlyRootFilesystem = $true; capabilities = @{ drop = @('ALL') } }
        resources = @{ requests = @{ cpu = '100m'; memory = '128Mi' }; limits = @{ cpu = '1'; memory = '512Mi' } }
    }
    if ($isAdmin) {
        $container.livenessProbe = @{ tcpSocket = @{ port = 'web' }; initialDelaySeconds = 10; periodSeconds = 10 }
        $container.readinessProbe = @{ tcpSocket = @{ port = 'web' }; initialDelaySeconds = 5; periodSeconds = 10 }
    } else {
        foreach ($probe in @('livenessProbe','readinessProbe')) {
            $path = if ($probe -eq 'livenessProbe') { '/health/live' } else { '/health/ready' }
            $container[$probe] = @{ httpGet = @{ path = $path; port = 'web'; scheme = 'HTTPS'; httpHeaders = @(@{ name = 'Host'; value = $appHost }) }; initialDelaySeconds = 10; periodSeconds = 10; timeoutSeconds = 5 }
        }
    }
    $pod = @{
        automountServiceAccountToken = !$isAdmin
        securityContext = @{ runAsNonRoot = $true; runAsUser = 1654; runAsGroup = 1654; fsGroup = 1654; seccompProfile = @{ type = 'RuntimeDefault' } }
        containers = @($container); volumes = $volumes
    }
    if (!$isAdmin) { $pod.serviceAccountName = 'arkya-realms' }
    if ($config.imagePullSecretName) { $pod.imagePullSecrets = @(@{ name = $config.imagePullSecretName }) }
    Add-Resource apps/v1 Deployment $name $ops @{ spec = @{
        replicas = 1; strategy = @{ type = 'Recreate' }; selector = @{ matchLabels = @{ app = $name } }
        template = @{ metadata = @{ labels = @{ app = $name } }; spec = $pod }
    } }
    Add-Resource v1 Service $name $ops @{ spec = @{ type = 'ClusterIP'; selector = @{ app = $name }; ports = @(@{ name = 'web'; port = $port; targetPort = 'web' }) } }
    $source = if ($isAdmin) {
        @{ namespaceSelector = @{ matchLabels = @{ 'kubernetes.io/metadata.name' = $config.gatewayNamespace } }; podSelector = @{ matchLabels = @{ 'app.kubernetes.io/name' = 'arkya-gateway' } } }
    } else { @{ podSelector = @{ matchLabels = @{ app = 'arkya-admin' } } } }
    Add-Resource networking.k8s.io/v1 NetworkPolicy "$name-ingress" $ops @{ spec = @{
        podSelector = @{ matchLabels = @{ app = $name } }; policyTypes = @('Ingress')
        ingress = @(@{ from = @($source); ports = @(@{ protocol = 'TCP'; port = $port }) })
    } }
}
foreach ($target in $config.deployment.targets) {
    Add-Resource v1 Namespace $target.namespace '' @{}
    Add-Resource rbac.authorization.k8s.io/v1 Role arkya-realm-deployer $target.namespace @{ rules = @(
        @{ apiGroups = @('apps'); resources = @('deployments'); verbs = @('get','create') },
        @{ apiGroups = @(''); resources = @('services'); verbs = @('get','create') }
    ) }
    Add-Resource rbac.authorization.k8s.io/v1 RoleBinding arkya-realm-deployer $target.namespace @{
        roleRef = @{ apiGroup = 'rbac.authorization.k8s.io'; kind = 'Role'; name = 'arkya-realm-deployer' }
        subjects = @(@{ kind = 'ServiceAccount'; name = 'arkya-realms'; namespace = $ops })
    }
}
$directory = $OutputDirectory
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$path = Join-Path $directory 'operations.json'
@{ apiVersion = 'v1'; kind = 'List'; items = $items.ToArray() } | ConvertTo-Json -Depth 60 | Set-Content -LiteralPath $path -Encoding utf8NoBOM
Write-Output "Rendered: $path"
Write-Output 'No cluster resources were changed. Provision referenced secrets and review the manifest before applying it.'
