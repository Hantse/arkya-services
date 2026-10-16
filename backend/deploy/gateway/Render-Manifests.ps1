#Requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string] $Configuration)

$ErrorActionPreference = 'Stop'
$config = Get-Content -LiteralPath $Configuration -Raw | ConvertFrom-Json -AsHashtable
if ($config.image -isnot [string] -or [string]::IsNullOrWhiteSpace($config.image) -or
    $config.image -match '(?i)replace|example|\s' -or
    $config.image -notmatch '(@sha256:[a-f0-9]{64}|:[A-Za-z0-9_][A-Za-z0-9_.-]{0,127})$' -or
    $config.image -match ':latest$') {
    throw 'Configure image with a digest or a versioned release tag, not latest.'
}
if ($config.trustedProxyNetworks -isnot [array] -or $config.trustedProxyNetworks.Count -eq 0) {
    throw 'Specify trustedProxyNetworks as a nonempty array of actual Traefik source CIDRs.'
}
foreach ($cidr in $config.trustedProxyNetworks) {
    if ($cidr -isnot [string] -or $cidr -ne $cidr.Trim()) { throw "Invalid proxy CIDR: $cidr" }
    $parts = $cidr.Split('/')
    $address = $null
    $prefix = 0
    if ($parts.Count -ne 2 -or ![Net.IPAddress]::TryParse($parts[0], [ref]$address) -or
        ![int]::TryParse($parts[1], [ref]$prefix)) { throw "Invalid proxy CIDR: $cidr" }
    $maxBits = if ($address.AddressFamily -eq [Net.Sockets.AddressFamily]::InterNetwork) { 32 } else { 128 }
    if ($prefix -lt 1 -or $prefix -gt $maxBits) { throw "Refusing unrestricted or invalid proxy CIDR: $cidr" }
    if ($maxBits -eq 128 -and $prefix -le 96) {
        $networkBytes = $address.GetAddressBytes()
        $mappedBytes = [Net.IPAddress]::Parse('::ffff:0.0.0.0').GetAddressBytes()
        $coversAllMapped = $true
        for ($bit = 0; $bit -lt $prefix; $bit++) {
            $byteIndex = [int][Math]::Floor($bit / 8)
            $mask = 1 -shl (7 - ($bit % 8))
            if (($networkBytes[$byteIndex] -band $mask) -ne ($mappedBytes[$byteIndex] -band $mask)) {
                $coversAllMapped = $false
                break
            }
        }
        if ($coversAllMapped) { throw "Refusing CIDR that trusts every IPv4-mapped address: $cidr" }
    }
}
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
if (($config.servicePort -isnot [long] -and $config.servicePort -isnot [int]) -or
    $config.servicePort -lt 1 -or $config.servicePort -gt 65535) { throw 'servicePort must be an integer between 1 and 65535.' }
if ($config.ContainsKey('serviceAnnotations') -and $config.serviceAnnotations -isnot [Collections.IDictionary]) {
    throw 'serviceAnnotations must be an object.'
}
if ($config.serviceAnnotations) {
    foreach ($key in $config.serviceAnnotations.Keys) {
        $value = $config.serviceAnnotations[$key]
        if ([string]::IsNullOrWhiteSpace($key) -or $value -isnot [string] -or
            "$key=$value" -match '(?i)replace|example') { throw 'Configure serviceAnnotations using string values, or leave the object empty.' }
    }
}
if ($config.imagePullSecretName -and ($config.imagePullSecretName -isnot [string] -or
    $config.imagePullSecretName -notmatch '^[a-z0-9]([-a-z0-9.]*[a-z0-9])?$' -or
    $config.imagePullSecretName.Length -gt 253 -or $config.imagePullSecretName -match '(?i)replace|example')) {
    throw 'Specify a valid imagePullSecretName, or leave it empty for a public image.'
}
$charactersEnabled = ![string]::IsNullOrWhiteSpace($config.charactersAddress)
if ($charactersEnabled) {
    $charactersUri = $null
    if ($config.charactersAddress -isnot [string] -or $config.charactersAddress -ne $config.charactersAddress.Trim() -or
        ![Uri]::TryCreate($config.charactersAddress, [UriKind]::Absolute, [ref]$charactersUri) -or
        $charactersUri.Scheme -notin @('http', 'https') -or $charactersUri.UserInfo -or
        $charactersUri.Query -or $charactersUri.Fragment -or $charactersUri.AbsolutePath -ne '/' -or
        $charactersUri.Host -match '(?i)replace|example') {
        throw 'charactersAddress must be an HTTP(S) service origin without credentials, query, fragment or path prefix.'
    }
}
if ($config.ContainsKey('adminAddress') -and $config.adminAddress -isnot [string]) {
    throw 'adminAddress must be a string, or empty to disable the backoffice.'
}
$adminEnabled = ![string]::IsNullOrWhiteSpace($config.adminAddress)
$adminHost = if ($config.ContainsKey('adminHost')) { $config.adminHost } else { 'backoffice.arkya.online' }
if ($adminEnabled) {
    $adminHostIp = $null
    if ($adminHost -isnot [string] -or $adminHost.Length -gt 253 -or
        $adminHost -cnotmatch '\A[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)+\z' -or
        [Net.IPAddress]::TryParse($adminHost, [ref]$adminHostIp) -or $adminHost -ieq 'api.arkya.online') {
        throw 'adminHost must be an exact DNS hostname distinct from api.arkya.online.'
    }
    $adminUri = $null
    if ($config.adminAddress -match '\s|\\' -or ![Uri]::IsWellFormedUriString($config.adminAddress, [UriKind]::Absolute) -or
        ![Uri]::TryCreate($config.adminAddress, [UriKind]::Absolute, [ref]$adminUri) -or
        $adminUri.Scheme -notin @('http', 'https') -or !$adminUri.Host -or $adminUri.UserInfo -or
        $adminUri.Query -or $adminUri.Fragment -or $adminUri.AbsolutePath -ne '/' -or
        $adminUri.Host -match '(?i)replace|example') {
        throw 'adminAddress must be an HTTP(S) service origin without credentials, query, fragment or path prefix.'
    }
}

$outputDirectory = Join-Path $PSScriptRoot '.local/production'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$literals = @("Gateway__CharactersEnabled=$($charactersEnabled.ToString().ToLowerInvariant())",
    "Gateway__AdminEnabled=$($adminEnabled.ToString().ToLowerInvariant())",
    $(if ($adminEnabled) { "AllowedHosts=api.arkya.online;$adminHost" } else { 'AllowedHosts=api.arkya.online' }))
for ($i = 0; $i -lt $config.trustedProxyNetworks.Count; $i++) {
    $literals += "ProxyTrust__KnownNetworks__$i=$($config.trustedProxyNetworks[$i])"
}
if ($charactersEnabled) {
    $literals += "ReverseProxy__Clusters__characters__Destinations__primary__Address=$($charactersUri.AbsoluteUri)"
}
if ($adminEnabled) {
    $literals += "Gateway__AdminHost=$adminHost"
    $literals += "Gateway__AdminAddress=$($adminUri.AbsoluteUri)"
}
$servicePatch = @{
    apiVersion = 'v1'; kind = 'Service'; metadata = @{ name = 'arkya-gateway' }
    spec = @{
        loadBalancerIP = $config.serviceAddress
        ports = @(@{ '$patch' = 'replace' }, @{ name = 'http'; port = $config.servicePort; targetPort = 'http'; protocol = 'TCP' })
    }
}
if ($config.serviceAnnotations) { $servicePatch.metadata.annotations = $config.serviceAnnotations }
$networkPatch = @{
    apiVersion = 'networking.k8s.io/v1'; kind = 'NetworkPolicy'; metadata = @{ name = 'arkya-gateway-ingress' }
    spec = @{ ingress = @(@{
        from = @($config.trustedProxyNetworks | ForEach-Object { @{ ipBlock = @{ cidr = $_ } } })
        ports = @(@{ protocol = 'TCP'; port = 8080 })
    }) }
}
$deploymentPatch = @{
    apiVersion = 'apps/v1'; kind = 'Deployment'; metadata = @{ name = 'arkya-gateway' }
    spec = @{ template = @{ spec = @{ containers = @(@{ name = 'gateway'; image = $config.image }) } } }
}
if ($config.imagePullSecretName) { $deploymentPatch.spec.template.spec.imagePullSecrets = @(@{ name = $config.imagePullSecretName }) }
$overlay = @{
    apiVersion = 'kustomize.config.k8s.io/v1beta1'; kind = 'Kustomization'; resources = @('../../base')
    configMapGenerator = @(@{ name = 'arkya-gateway-config'; behavior = 'merge'; literals = $literals })
    patches = @(@{ patch = ($servicePatch | ConvertTo-Json -Depth 20) },
        @{ patch = ($networkPatch | ConvertTo-Json -Depth 20) }, @{ patch = ($deploymentPatch | ConvertTo-Json -Depth 20) })
}
$overlay | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath (Join-Path $outputDirectory 'kustomization.yaml') -Encoding utf8NoBOM
$rendered = & kubectl kustomize $outputDirectory
if ($LASTEXITCODE -ne 0) { throw 'Kustomize rendering failed.' }
if (($rendered -join "`n") -match 'replace-before-deploy') { throw 'Unresolved placeholder in rendered manifests.' }
$manifestPath = Join-Path $outputDirectory 'gateway.yaml'
$rendered | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM
Write-Output "Rendered: $manifestPath"
Write-Output "Expected backend: http://$($config.serviceAddress):$($config.servicePort). This address must belong only to the gateway."
Write-Output 'No cluster resources were changed. Configure Identity with its gateway exposure profile before enabling this route.'
