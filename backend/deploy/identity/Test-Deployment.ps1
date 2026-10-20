#Requires -Version 7.2
[CmdletBinding()]
param(
    [switch] $PrepareWebsiteConfig,
    [ValidateSet('https://identity.arkya.online', 'https://api.arkya.online')]
    [string] $Origin = 'https://identity.arkya.online',
    [ValidateSet('https://arkya.online', 'https://arkya-atlas.easykrill33.chatgpt.site')]
    [string] $SiteOrigin = 'https://arkya-atlas.easykrill33.chatgpt.site'
)

$ErrorActionPreference = 'Stop'
$expectedService = if ($Origin -eq 'https://api.arkya.online') { 'arkya-gateway' } else { 'arkya-identity' }
$handler = [Net.Http.HttpClientHandler]::new()
$handler.AllowAutoRedirect = $false
$handler.UseCookies = $false
$client = [Net.Http.HttpClient]::new($handler)
$client.Timeout = [TimeSpan]::FromSeconds(15)

function Invoke-IdentityRequest([string] $Method, [string] $Path, [hashtable] $Headers = @{}, [string] $Body = '') {
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::new($Method), "$origin$Path")
    foreach ($key in $Headers.Keys) { $null = $request.Headers.TryAddWithoutValidation($key, [string]$Headers[$key]) }
    if ($Body) { $request.Content = [Net.Http.StringContent]::new($Body, [Text.Encoding]::UTF8, 'application/json') }
    try {
        $response = $client.SendAsync($request).GetAwaiter().GetResult()
        try {
            $headersOut = @{}
            foreach ($header in $response.Headers) { $headersOut[$header.Key] = $header.Value -join ',' }
            return @{ Status = [int]$response.StatusCode; Headers = $headersOut; Body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() }
        } finally { $response.Dispose() }
    } finally { $request.Dispose() }
}
function Assert-Check([bool] $Condition, [string] $Message) { if (!$Condition) { throw $Message } }
function Assert-Cors($Response) {
    Assert-Check ($Response.Headers['Access-Control-Allow-Origin'] -ceq $siteOrigin) 'The public site origin is not allowed by Identity CORS.'
    Assert-Check ($Response.Headers['Access-Control-Allow-Credentials'] -ne 'true') 'Unexpected credentialed CORS policy.'
}
try {
    $health = Invoke-IdentityRequest GET /health
    Assert-Check ($health.Status -eq 200) "Public API HTTPS health returned $($health.Status)."
    Assert-Check (($health.Body | ConvertFrom-Json).service -eq $expectedService) "Unexpected service at $Origin."
    $options = Invoke-IdentityRequest OPTIONS /v1/auth/discord/challenges @{
        Origin = $siteOrigin; 'Access-Control-Request-Method' = 'POST'; 'Access-Control-Request-Headers' = 'content-type'
    }
    Assert-Check ($options.Status -in 200,204) 'The login preflight request was rejected.'
    Assert-Cors $options
    Assert-Check ($options.Headers['Access-Control-Allow-Methods'] -match 'POST') 'POST is missing from the CORS preflight response.'
    Assert-Check ($options.Headers['Access-Control-Allow-Headers'] -match 'content-type') 'Content-Type is missing from the CORS preflight response.'
    $me = Invoke-IdentityRequest GET /v1/auth/me @{ Origin = $siteOrigin }
    Assert-Check ($me.Status -eq 401) 'An unauthenticated account request must return 401.'
    Assert-Cors $me
    $foreign = Invoke-IdentityRequest OPTIONS /v1/auth/discord/challenges @{
        Origin = 'https://untrusted.invalid'; 'Access-Control-Request-Method' = 'POST'
    }
    Assert-Check (!$foreign.Headers.ContainsKey('Access-Control-Allow-Origin')) 'An unconfigured origin is allowed by CORS.'
    foreach ($path in @('/internal/sessions/introspect', '/health/live', '/health/ready', '/v1/auth/discord/social/config')) {
        $hidden = Invoke-IdentityRequest GET $path
        Assert-Check ($hidden.Status -eq 404) "The ingress exposes an unexpected route: $path (status $($hidden.Status))."
    }
    $internalPost = Invoke-IdentityRequest POST /internal/sessions/introspect @{} '{}'
    Assert-Check ($internalPost.Status -eq 404) 'The public entry point exposes the internal introspection endpoint.'
    $verifier = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).TrimEnd('=').Replace('+','-').Replace('/','_')
    $proof = [Convert]::ToBase64String([Security.Cryptography.SHA256]::HashData([Text.Encoding]::ASCII.GetBytes($verifier))).TrimEnd('=').Replace('+','-').Replace('/','_')
    $challengeResponse = Invoke-IdentityRequest POST /v1/auth/discord/challenges @{ Origin = $siteOrigin } (@{ codeChallenge = $proof } | ConvertTo-Json -Compress)
    Assert-Check ($challengeResponse.Status -eq 200) 'Challenge creation failed; check MongoDB, the ingress and Identity configuration.'
    Assert-Cors $challengeResponse
    $challenge = $challengeResponse.Body | ConvertFrom-Json
    Assert-Check ($challenge.challengeId -cmatch '^[a-f0-9]{32}$') 'Unexpected challenge ID.'
    Assert-Check ($challenge.authorizationUrl -ceq "$origin/v1/auth/discord/authorize/$($challenge.challengeId)") 'The authorization URL does not target the expected Identity endpoint.'
    Assert-Check ($challenge.pollIntervalSeconds -ge 5 -and $challenge.pollIntervalSeconds -le 60) 'Unexpected polling interval.'
    Start-Sleep -Seconds $challenge.pollIntervalSeconds
    $pending = Invoke-IdentityRequest POST /v1/auth/discord/exchange @{ Origin = $siteOrigin } (@{ challengeId = $challenge.challengeId; codeVerifier = $verifier } | ConvertTo-Json -Compress)
    Assert-Check ($pending.Status -eq 202 -and ($pending.Body | ConvertFrom-Json).status -eq 'pending') 'Challenge polling did not return the expected pending state.'
    Assert-Cors $pending
    Assert-Check ($pending.Headers['Access-Control-Expose-Headers'] -match 'Retry-After') 'Retry-After must be exposed to the browser.'
    Write-Output "HTTPS, public routes, CORS for $SiteOrigin, MongoDB challenge creation and polling passed. No Discord account was connected."
    if ($PrepareWebsiteConfig) {
        $directory = Join-Path $PSScriptRoot '.local'
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
        $path = Join-Path $directory 'auth-config.js'
        "window.ArkyaAuthConfig = Object.freeze({ identityBaseUrl: '$origin' });" | Set-Content -LiteralPath $path -Encoding utf8NoBOM
        Write-Output "Frontend configuration prepared: $path"
        Write-Output 'Update the privacy notice, copy this file to sites/arkya/dist/auth-config.js and publish the site. Complete one real Discord sign-in afterwards.'
    }
} finally {
    $client.Dispose()
    $handler.Dispose()
    $verifier = $null
}
