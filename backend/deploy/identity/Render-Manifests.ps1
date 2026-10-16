#Requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string] $Configuration)

$ErrorActionPreference = 'Stop'
$config = Get-Content -LiteralPath $Configuration -Raw | ConvertFrom-Json -AsHashtable
$exposure = if ($config.ContainsKey('exposure')) { $config.exposure } else { 'ingress' }
if ($exposure -notin @('ingress', 'external-traefik', 'gateway')) { throw "Use exposure 'ingress', 'external-traefik' or 'gateway'." }
$requiredNames = @('image', 'storageClass')
if ($exposure -eq 'ingress') { $requiredNames += @('ingressClass', 'ingressNamespace', 'tlsSecretName') }
foreach ($name in $requiredNames) {
    if ([string]::IsNullOrWhiteSpace($config[$name]) -or $config[$name] -match '(?i)replace|example') {
        throw "Configure '$name' before rendering."
    }
}
if ($config.image -notmatch '(@sha256:[a-f0-9]{64}|:[A-Za-z0-9_][A-Za-z0-9_.-]{0,127})$' -or $config.image -match ':latest$') {
    throw 'Use an image digest or a versioned release tag, not latest.'
}
if ($exposure -eq 'ingress') {
    if ($config.ingressPodLabels -isnot [Collections.IDictionary] -or $config.ingressPodLabels.Count -eq 0) { throw 'Specify ingressPodLabels from the controller pods.' }
    if (($config.ingressPodLabels | ConvertTo-Json -Compress) -match '(?i)replace|example') { throw 'Configure ingressPodLabels.' }
}
if ($config.trustedProxyNetworks -isnot [array] -or $config.trustedProxyNetworks.Count -eq 0) { throw 'Specify trustedProxyNetworks as a nonempty array of actual proxy source CIDRs.' }
foreach ($cidr in $config.trustedProxyNetworks) {
    if ($cidr -isnot [string] -or $cidr -ne $cidr.Trim()) { throw "Invalid proxy CIDR: $cidr" }
    $parts = $cidr.Split('/')
    $address = $null
    $prefix = 0
    if ($parts.Count -ne 2 -or ![Net.IPAddress]::TryParse($parts[0], [ref]$address) -or ![int]::TryParse($parts[1], [ref]$prefix)) { throw "Invalid proxy CIDR: $cidr" }
    $maxBits = if ($address.AddressFamily -eq [Net.Sockets.AddressFamily]::InterNetwork) { 32 } else { 128 }
    if ($prefix -lt 1 -or $prefix -gt $maxBits) { throw "Refusing unrestricted or invalid proxy CIDR: $cidr" }
    if ($maxBits -eq 128 -and $prefix -le 96) {
        $networkBytes = $address.GetAddressBytes()
        $mappedBytes = [Net.IPAddress]::Parse('::ffff:0.0.0.0').GetAddressBytes()
        $coversAllMapped = $true
        for ($bit = 0; $bit -lt $prefix; $bit++) {
            $byteIndex = [int][Math]::Floor($bit / 8)
            $mask = 1 -shl (7 - ($bit % 8))
            if (($networkBytes[$byteIndex] -band $mask) -ne ($mappedBytes[$byteIndex] -band $mask)) { $coversAllMapped = $false; break }
        }
        if ($coversAllMapped) { throw "Refusing CIDR that trusts every IPv4-mapped address: $cidr" }
    }
}
foreach ($name in @('clusterIssuer', 'imagePullSecretName')) {
    if ($config[$name] -match '(?i)replace|example') { throw "Configure '$name' or leave it empty." }
}
if ($exposure -eq 'external-traefik') {
    $serviceAddress = $null
    if ($config.serviceAddress -isnot [string] -or $config.serviceAddress -ne $config.serviceAddress.Trim() -or
        ![Net.IPAddress]::TryParse($config.serviceAddress, [ref]$serviceAddress) -or
        [Net.IPAddress]::IsLoopback($serviceAddress) -or $serviceAddress.Equals([Net.IPAddress]::Any) -or
        $serviceAddress.Equals([Net.IPAddress]::IPv6Any) -or $serviceAddress.IsIPv6Multicast -or
        $serviceAddress.IsIPv6LinkLocal -or $serviceAddress.IsIPv4MappedToIPv6 -or
        ($serviceAddress.AddressFamily -eq [Net.Sockets.AddressFamily]::InterNetwork -and $serviceAddress.GetAddressBytes()[0] -ge 224)) {
        throw 'Specify serviceAddress as a reachable unicast IP for the cluster LoadBalancer.'
    }
    if ($serviceAddress.ToString() -cne $config.serviceAddress) { throw 'Use the canonical IP spelling for serviceAddress.' }
    if ($config.servicePort -isnot [long] -and $config.servicePort -isnot [int]) { throw 'Specify servicePort as an integer.' }
    if ($config.servicePort -lt 1 -or $config.servicePort -gt 65535) { throw 'servicePort must be between 1 and 65535.' }
    if ($config.ContainsKey('serviceAnnotations') -and $config.serviceAnnotations -isnot [Collections.IDictionary]) { throw 'serviceAnnotations must be an object.' }
    if ($config.serviceAnnotations) {
        foreach ($key in $config.serviceAnnotations.Keys) {
            $value = $config.serviceAnnotations[$key]
            if ([string]::IsNullOrWhiteSpace($key) -or $value -isnot [string] -or
                "$key=$value" -match '(?i)replace|example') { throw 'Configure serviceAnnotations using string values, or leave the object empty.' }
        }
    }
}

$outputName = switch ($exposure) { 'external-traefik' { 'external-traefik' } 'gateway' { 'gateway' } default { 'production' } }
$outputDirectory = Join-Path $PSScriptRoot ".local/$outputName"
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$literals = @()
for ($i = 0; $i -lt $config.trustedProxyNetworks.Count; $i++) {
    $literals += "ReverseProxy__KnownNetworks__$i=$($config.trustedProxyNetworks[$i])"
}
$patches = @()
if ($exposure -in @('external-traefik', 'gateway')) {
    $ingressPatch = @{
        apiVersion = 'networking.k8s.io/v1'; kind = 'Ingress'; metadata = @{ name = 'arkya-identity' }; '$patch' = 'delete'
    }
    $servicePatch = @{
        apiVersion = 'v1'; kind = 'Service'; metadata = @{ name = 'arkya-identity' }
        spec = @{
            type = 'LoadBalancer'; loadBalancerIP = $config.serviceAddress; externalTrafficPolicy = 'Local'
            ports = @(@{ '$patch' = 'replace' }, @{ name = 'http'; port = $config.servicePort; targetPort = 'http'; protocol = 'TCP' })
        }
    }
    if ($exposure -eq 'gateway') {
        $servicePatch.spec = @{
            type = 'ClusterIP'
            ports = @(@{ '$patch' = 'replace' }, @{ name = 'http'; port = 8080; targetPort = 'http'; protocol = 'TCP' })
        }
        $literals += @('AllowedHosts=api.arkya.online', 'Discord__PublicBaseUrl=https://api.arkya.online',
            'Discord__RedirectUri=https://api.arkya.online/v1/auth/discord/callback')
    } elseif ($config.serviceAnnotations) { $servicePatch.metadata.annotations = $config.serviceAnnotations }
    $patches += @{ patch = ($servicePatch | ConvertTo-Json -Depth 20) }
    $networkSources = @(if ($exposure -eq 'gateway') {
        @(@{ podSelector = @{ matchLabels = @{ 'app.kubernetes.io/name' = 'arkya-gateway' } } })
    } else {
        @($config.trustedProxyNetworks | ForEach-Object { @{ ipBlock = @{ cidr = $_ } } })
    })
} else {
    $annotations = if ($config.ingressAnnotations) { $config.ingressAnnotations } else { @{} }
    if ($config.clusterIssuer) { $annotations['cert-manager.io/cluster-issuer'] = $config.clusterIssuer }
    $ingressPatch = @{
        apiVersion = 'networking.k8s.io/v1'; kind = 'Ingress'; metadata = @{ name = 'arkya-identity'; annotations = $annotations }
        spec = @{ ingressClassName = $config.ingressClass; tls = @(@{ hosts = @('identity.arkya.online'); secretName = $config.tlsSecretName }) }
    }
    $networkSources = @(@{
        namespaceSelector = @{ matchLabels = @{ 'kubernetes.io/metadata.name' = $config.ingressNamespace } }
        podSelector = @{ matchLabels = $config.ingressPodLabels }
    })
}
$networkPatch = @{
    apiVersion = 'networking.k8s.io/v1'; kind = 'NetworkPolicy'; metadata = @{ name = 'arkya-identity-ingress' }
    spec = @{ ingress = @(@{ from = $networkSources; ports = @(@{ protocol = 'TCP'; port = 8080 }) }) }
}
$volumePatch = @{
    apiVersion = 'v1'; kind = 'PersistentVolumeClaim'; metadata = @{ name = 'arkya-identity-keys' }
    spec = @{ storageClassName = $config.storageClass }
}
$deploymentPatch = @{
    apiVersion = 'apps/v1'; kind = 'Deployment'; metadata = @{ name = 'arkya-identity' }
    spec = @{ template = @{ spec = @{ containers = @(@{ name = 'identity'; image = $config.image }) } } }
}
if ($config.imagePullSecretName) { $deploymentPatch.spec.template.spec.imagePullSecrets = @(@{ name = $config.imagePullSecretName }) }
if ($exposure -eq 'gateway') {
    $containerPatch = $deploymentPatch.spec.template.spec.containers[0]
    foreach ($probe in @('startupProbe', 'livenessProbe', 'readinessProbe')) {
        $containerPatch[$probe] = @{ httpGet = @{ httpHeaders = @(@{ name = 'Host'; value = 'api.arkya.online' }) } }
    }
}
$patches += @(@{ patch = ($ingressPatch | ConvertTo-Json -Depth 20) }, @{ patch = ($networkPatch | ConvertTo-Json -Depth 20) },
    @{ patch = ($volumePatch | ConvertTo-Json -Depth 20) }, @{ patch = ($deploymentPatch | ConvertTo-Json -Depth 20) })
$overlay = @{
    apiVersion = 'kustomize.config.k8s.io/v1beta1'; kind = 'Kustomization'; resources = @('../../base')
    configMapGenerator = @(@{ name = 'arkya-identity-config'; behavior = 'merge'; literals = $literals })
    patches = $patches
}
$overlay | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath (Join-Path $outputDirectory 'kustomization.yaml') -Encoding utf8NoBOM
$rendered = & kubectl kustomize $outputDirectory
if ($LASTEXITCODE -ne 0) { throw 'Kustomize rendering failed.' }
if (($rendered -join "`n") -match 'replace-before-deploy') { throw 'Unresolved placeholder in rendered manifests.' }
$manifestPath = Join-Path $outputDirectory 'identity.yaml'
$rendered | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM
Write-Output "Rendered: $manifestPath"
if ($exposure -eq 'external-traefik') {
    Write-Output "Expected backend: http://$($config.serviceAddress):$($config.servicePort). Verify the LoadBalancer allocates this address and preserves the Traefik source IP."
    Write-Output 'No cluster resources were changed. Provision the application Secret and configure TLS/routing on external Traefik before opening access.'
} elseif ($exposure -eq 'gateway') {
    Write-Output 'Identity is internal on port 8080; public OAuth origin is https://api.arkya.online. Apply the separate gateway manifests for public access.'
    Write-Output 'No cluster resources were changed. Remove any previous public Identity Ingress and release its LoadBalancer IP before assigning that IP to the gateway.'
} else {
    Write-Output 'No cluster resources were changed. The Secret and TLS certificate must exist before rollout.'
}
