[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ExecutablePath,
    [Parameter(Mandatory)][string]$ServerExecutablePath,
    [string]$OutputRoot,
    [ValidateRange(15, 35)][int]$DurationSeconds = 25
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-Check([bool]$Condition, [string]$Message) {
    $checks.Add([pscustomobject]@{ Passed = $Condition; Message = $Message })
    if (-not $Condition) { throw $Message }
    Write-Host "PASS $Message"
}

function New-TestSecret {
    return [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
}

function Get-FreeTcpPort {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    try { $listener.Start(); return ([Net.IPEndPoint]$listener.LocalEndpoint).Port }
    finally { $listener.Stop() }
}

function Start-OwnedProcess([string]$Name, [string]$Path, [string[]]$Arguments, [hashtable]$EnvironmentValues,
    [string]$WorkingDirectory, [switch]$CaptureOutput) {
    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName = $Path
    $info.WorkingDirectory = $WorkingDirectory
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $info.RedirectStandardOutput = [bool]$CaptureOutput
    $info.RedirectStandardError = [bool]$CaptureOutput
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    foreach ($name in $EnvironmentValues.Keys) { $info.Environment[$name] = [string]$EnvironmentValues[$name] }
    $process = [Diagnostics.Process]::Start($info)
    $null = $process.Handle
    $worker = [pscustomobject]@{
        Name = $Name; Process = $process; ProcessId = $process.Id
        StartTimeUtcTicks = $process.StartTime.ToUniversalTime().Ticks
        ExecutablePath = [IO.Path]::GetFullPath($Path)
        OutputTask = if ($CaptureOutput) { $process.StandardOutput.ReadToEndAsync() } else { $null }
        ErrorTask = if ($CaptureOutput) { $process.StandardError.ReadToEndAsync() } else { $null }
    }
    $owned.Add($worker)
    return $worker
}

function Stop-OwnedProcess($Worker) {
    $process = Get-Process -Id $Worker.ProcessId -ErrorAction SilentlyContinue
    if ($null -eq $process) { return }
    try {
        if (-not $process.HasExited -and $process.StartTime.ToUniversalTime().Ticks -eq $Worker.StartTimeUtcTicks -and
            $process.Path -eq $Worker.ExecutablePath) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            $null = $process.WaitForExit(5000)
        }
    } finally { $process.Dispose() }
}

function Wait-Healthy([string]$Url, $Worker) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(30)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if ($Worker.Process.HasExited) { throw "$($Worker.Name) exited before its health endpoint became ready." }
        try {
            $response = $api.GetAsync("$Url/health").GetAwaiter().GetResult()
            try { if ([int]$response.StatusCode -eq 200) { return } } finally { $response.Dispose() }
        } catch [Net.Http.HttpRequestException] { }
        Start-Sleep -Milliseconds 250
    }
    throw "$($Worker.Name) did not become healthy within 30 seconds."
}

function Invoke-Json([string]$Url, [string]$HeaderName, [string]$HeaderValue) {
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Get, $Url)
    $request.Headers.Add($HeaderName, $HeaderValue)
    try {
        $response = $api.SendAsync($request).GetAwaiter().GetResult()
        try {
            if ([int]$response.StatusCode -ne 200) { throw "Expected HTTP 200 from $Url." }
            return $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
        } finally { $response.Dispose() }
    } finally { $request.Dispose() }
}

function Get-FormToken([string]$Path) {
    $response = $browser.GetAsync("$adminUrl$Path").GetAwaiter().GetResult()
    try {
        if ([int]$response.StatusCode -ne 200) { throw "Cannot load admin form $Path." }
        $html = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        $token = [regex]::Match($html, '<input\b[^>]*name="__RequestVerificationToken"[^>]*value="([^"]+)"')
        if (-not $token.Success) { throw "Admin form $Path contains no antiforgery token." }
        return [Net.WebUtility]::HtmlDecode($token.Groups[1].Value)
    } finally { $response.Dispose() }
}

function Submit-AdminForm([string]$Path, [hashtable]$Fields, [string]$TokenPage = '/') {
    $values = [Collections.Generic.Dictionary[string, string]]::new()
    $values['__RequestVerificationToken'] = Get-FormToken $TokenPage
    foreach ($name in $Fields.Keys) { $values[$name] = [string]$Fields[$name] }
    $body = [Net.Http.FormUrlEncodedContent]::new($values)
    try {
        $response = $browser.PostAsync("$adminUrl$Path", $body).GetAwaiter().GetResult()
        try {
            if ([int]$response.StatusCode -ne 302) { throw "Admin form $Path failed (HTTP $([int]$response.StatusCode))." }
            if ($response.Headers.Location.OriginalString -ne '/') { throw "Admin form $Path returned an unexpected redirect." }
        } finally { $response.Dispose() }
    } finally { $body.Dispose() }
}

function Save-AndPublish([long]$Revision, [int]$RequiredKills) {
    Submit-AdminForm '/?handler=Save' @{
        Revision = $Revision; 'Quests[0].QuestId' = 'kill-monsters'; 'Quests[0].Title' = 'La menace du bois'
        'Quests[0].Description' = 'Vaincre les monstres du prototype.'; 'Quests[0].RequiredKills' = $RequiredKills
    }
    Submit-AdminForm '/?handler=Publish' @{ Revision = $Revision + 1 }
    $catalogs = @(Invoke-Json "$contentUrl/admin/content/catalogs" 'X-Arkya-Admin-Key' $contentAdminKey)
    $catalog = @($catalogs | Where-Object { $_.quests[0].requiredKills -eq $RequiredKills })
    Assert-Check ($catalog.Count -eq 1) "Admin published one catalog with target $RequiredKills"
    return $catalog[0]
}

function Save-AdminPreview {
    $response = $browser.GetAsync("$adminUrl/").GetAwaiter().GetResult()
    try {
        if ([int]$response.StatusCode -ne 200) { throw 'Cannot capture the authenticated admin preview.' }
        $html = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        $html = [regex]::Replace($html,
            '(<input\b[^>]*\bname="__RequestVerificationToken"[^>]*\bvalue=")[^"]*(")', '$1$2')
        $html = [regex]::Replace($html, 'href="/admin\.css(?:\?[^"]*)?"', 'href="./admin.css"')
        foreach ($secret in $secrets) {
            if ($html.Contains($secret)) { throw 'The admin preview unexpectedly contained a credential.' }
        }
        if ([regex]::IsMatch($html, 'name="__RequestVerificationToken"[^>]*value="[^"]+"')) {
            throw 'The admin preview still contains an antiforgery token.'
        }
        $previewDirectory = Join-Path $runDirectory 'admin-preview'
        New-Item -ItemType Directory -Path $previewDirectory -Force | Out-Null
        $html | Set-Content -LiteralPath (Join-Path $previewDirectory 'index.html') -Encoding utf8
        Copy-Item -LiteralPath (Join-Path $repositoryRoot 'backend/src/Arkya.Admin/wwwroot/admin.css') -Destination (Join-Path $previewDirectory 'admin.css')
        Assert-Check $true 'A static admin preview was saved without cookies, credentials or antiforgery token values'
    } finally { $response.Dispose() }
}

function Set-QuestEnvironment([string]$Version) {
    $values = @{
        ARKYA_CONTENT_URL = $contentUrl; ARKYA_CONTENT_SERVICE_KEY = $contentServiceKey; ARKYA_CONTENT_VERSION = $Version
        ARKYA_CHARACTERS_URL = $charactersUrl; ARKYA_CHARACTERS_SERVICE_KEY = $charactersServiceKey
        ARKYA_CHARACTER_1_ID = $characterOne; ARKYA_CHARACTER_2_ID = $characterTwo
    }
    foreach ($name in $values.Keys) { [Environment]::SetEnvironmentVariable($name, $values[$name], 'Process') }
}

function Assert-CleanUnityLog([string]$Path, [string]$Name) {
    Assert-Check (Test-Path -LiteralPath $Path -PathType Leaf) "$Name produced a runtime log"
    $text = [IO.File]::ReadAllText($Path)
    foreach ($secret in $secrets) { Assert-Check (-not $text.Contains($secret)) "$Name did not log a service credential" }
    $pattern = '(?im)^\s*(?:(?:[A-Za-z_]\w*\.)*(?:[A-Za-z_]\w*)?Exception\s*:|Unhandled\s+exception\b|Assertion(?:\s+failed|\s+failure|Exception)\b|Assert\s+failed\b|(?:Fatal\s+)?Error\s*:|Fatal(?:\s+error)?\b|\[(?:error|exception|assertion|fatal)\]|Shader\s+error\b|Burst\s+error\b|Crash!!!|Received\s+signal\s+SIG(?:SEGV|ABRT)|Access\s+violation\b|UnityEngine\.Debug:(?:LogError|LogAssertion)\s*\()'
    Assert-Check (-not [regex]::IsMatch($text, $pattern)) "$Name logged no runtime errors, assertions or exceptions"
}

function Run-QuestScenario([string]$Version, [string]$AcceptedVersion, [string]$Label) {
    Set-QuestEnvironment $Version
    $run = & (Join-Path $repositoryRoot 'scripts/area-prototype/Start-AreaPrototype.ps1') `
        -ExecutablePath $resolvedExecutable -ServerExecutablePath $resolvedServerExecutable `
        -QuestScenario -Automated -HeadlessClients -DuplicateImpacts -DurationSeconds $DurationSeconds `
        -OutputRoot (Join-Path $runDirectory $Label)
    $scenarioWorkers = [Collections.Generic.List[object]]::new()
    foreach ($worker in $run.Workers) {
        $process = Get-Process -Id $worker.ProcessId -ErrorAction Stop
        if ($process.StartTime.ToUniversalTime().Ticks -ne $worker.StartTimeUtcTicks) { throw 'Prototype worker identity changed.' }
        $null = $process.Handle
        $ownedWorker = [pscustomobject]@{
            Name = $worker.Name; Process = $process; ProcessId = $process.Id
            StartTimeUtcTicks = $worker.StartTimeUtcTicks; ExecutablePath = $worker.ExecutablePath
            OutputTask = $null; ErrorTask = $null
        }
        $owned.Add($ownedWorker)
        $scenarioWorkers.Add($ownedWorker)
    }
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(60)
    $nextStatus = [DateTimeOffset]::UtcNow
    while (@($scenarioWorkers | Where-Object { -not $_.Process.HasExited }).Count -gt 0) {
        if ([DateTimeOffset]::UtcNow -gt $deadline) { throw "$Label exceeded its 60 second deadline." }
        if ([DateTimeOffset]::UtcNow -ge $nextStatus) { Write-Host "Waiting for $Label..."; $nextStatus = [DateTimeOffset]::UtcNow.AddSeconds(10) }
        Start-Sleep -Milliseconds 250
    }
    $exits = @($scenarioWorkers | ForEach-Object {
        Assert-Check ($_.Process.ExitCode -eq 0) "$Label $($_.Name) exited with code zero"
        [pscustomobject]@{ Worker = $_.Name; ExitCode = $_.Process.ExitCode }
    })
    $exits | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run.RunDirectory 'process-exits.json') -Encoding utf8
    $reports = @{}
    foreach ($worker in $run.Workers) {
        Assert-CleanUnityLog $worker.LogPath "$Label $($worker.Name)"
        $reportPath = Join-Path $run.RunDirectory "$($worker.Name).json"
        Assert-Check (Test-Path -LiteralPath $reportPath -PathType Leaf) "$Label $($worker.Name) produced its report"
        $reports[$worker.Name] = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    }
    foreach ($area in 0, 1) {
        $server = $reports["server-$area"]
        $expectedKills = if ($area -eq 0) { 3 } else { 0 }
        Assert-Check ([string]$server.CatalogVersion -eq $Version) "$Label area $area loaded the pinned catalog"
        Assert-Check ([string]::IsNullOrEmpty($server.Failure)) "$Label area $area reported no simulation failure"
        Assert-Check ($server.PendingQuestSaves -eq 0 -and $server.PendingKillCredits -eq 0 -and $server.FailedQuestSaveAttempts -eq 0) "$Label area $area drained quest persistence and kill credits"
        $quest = @($server.Quests | Where-Object { $_.QuestId -eq 'kill-monsters' })
        Assert-Check ($quest.Count -eq 1) "$Label area $area owns one matching quest"
        Assert-Check ($quest[0].PlayerId -eq ($area + 1) -and $quest[0].RequiredKills -eq 3 -and $quest[0].Kills -eq $expectedKills -and
            $quest[0].Completed -eq ($area -eq 0) -and $quest[0].CatalogVersion -eq $AcceptedVersion) "$Label player $($area + 1) preserved the accepted objective and authoritative progress"
        $client = $reports["client-$($area + 1)"]
        $observed = @($client.Quests | Where-Object { $_.QuestId -eq 'kill-monsters' })
        Assert-Check ($client.ConnectedAreas -eq 2 -and $client.Frames -gt 10) "$Label client $($area + 1) connected and rendered"
        Assert-Check ($observed.Count -eq 1 -and $observed[0].RequiredKills -eq 3 -and $observed[0].Kills -eq $expectedKills -and
            $observed[0].Completed -eq ($area -eq 0) -and $observed[0].CatalogVersion -eq $AcceptedVersion) "$Label client $($area + 1) observed its own quest progress"
        $character = if ($area -eq 0) { $characterOne } else { $characterTwo }
        $persisted = Invoke-Json "$charactersUrl/internal/characters/$character/quests" 'X-Arkya-Service-Key' $charactersServiceKey
        $entry = @($persisted.quests | Where-Object { $_.questId -eq 'kill-monsters' })
        Assert-Check ($persisted.characterId -eq $character -and $entry.Count -eq 1 -and $entry[0].requiredKills -eq 3 -and
            $entry[0].kills -eq $expectedKills -and $entry[0].completed -eq ($area -eq 0) -and $entry[0].catalogVersion -eq $AcceptedVersion) "$Label player $($area + 1) has matching durable Characters progress"
    }
    $duplicateImpacts = [int]$reports['server-0'].DuplicateImpactsIgnored + [int]$reports['server-1'].DuplicateImpactsIgnored
    Assert-Check ($duplicateImpacts -gt 0) "$Label repeated impact messages were deduplicated"
    return [pscustomobject]@{ Label = $Label; RunId = $run.RunId; RunDirectory = $run.RunDirectory; CatalogVersion = $Version; AcceptedVersion = $AcceptedVersion; ProcessExits = $exits }
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$resolvedExecutable = (Resolve-Path -LiteralPath $ExecutablePath).Path
$resolvedServerExecutable = (Resolve-Path -LiteralPath $ServerExecutablePath).Path
if ([string]::IsNullOrWhiteSpace($OutputRoot)) { $OutputRoot = Join-Path $repositoryRoot 'artifacts/content-workbench/runs' }
$runDirectory = Join-Path ([IO.Path]::GetFullPath($OutputRoot)) ((Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
$checks = [Collections.Generic.List[object]]::new()
$owned = [Collections.Generic.List[object]]::new()
$scenarios = [Collections.Generic.List[object]]::new()
$environmentNames = @('ARKYA_CONTENT_URL', 'ARKYA_CONTENT_SERVICE_KEY', 'ARKYA_CONTENT_VERSION', 'ARKYA_CHARACTERS_URL',
    'ARKYA_CHARACTERS_SERVICE_KEY', 'ARKYA_CHARACTER_1_ID', 'ARKYA_CHARACTER_2_ID')
$previousEnvironment = @{}
foreach ($name in $environmentNames) { $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
$contentServiceKey = New-TestSecret
$charactersServiceKey = New-TestSecret
$contentAdminKey = New-TestSecret
$adminPassword = New-TestSecret
$identityKey = New-TestSecret
$secrets = @($contentServiceKey, $charactersServiceKey, $contentAdminKey, $adminPassword, $identityKey)
$containerName = 'arkya-content-e2e-' + [Guid]::NewGuid().ToString('N').Substring(0, 12)
$containerCreated = $false
$characterOne = [Guid]::NewGuid().ToString('D')
$characterTwo = [Guid]::NewGuid().ToString('D')
$databaseSuffix = [Guid]::NewGuid().ToString('N')
$contentDatabase = "arkya_content_e2e_$databaseSuffix"
$charactersDatabase = "arkya_characters_e2e_$databaseSuffix"
$api = [Net.Http.HttpClient]::new([Net.Http.HttpClientHandler]::new())
$api.Timeout = [TimeSpan]::FromSeconds(3)
$browserHandler = [Net.Http.HttpClientHandler]::new()
$browserHandler.AllowAutoRedirect = $false
$browserHandler.CookieContainer = [Net.CookieContainer]::new()
$browser = [Net.Http.HttpClient]::new($browserHandler)
$browser.Timeout = [TimeSpan]::FromSeconds(10)
$passed = $false
$failure = $null
$invalidCatalogResult = $null
try {
    $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
    $null = Get-Command docker -ErrorAction Stop
    $null = Get-Command Get-NetUDPEndpoint -ErrorAction Stop
    foreach ($project in 'Arkya.Content.Api', 'Arkya.Characters.Api', 'Arkya.Admin') {
        & $dotnet build (Join-Path $repositoryRoot "backend/src/$project/$project.csproj") --verbosity minimal
        if ($LASTEXITCODE -ne 0) { throw "Backend build failed: $project." }
    }
    $dockerRun = & docker run --detach --rm --name $containerName --publish '127.0.0.1::27017' 'mongo:8.0.15' --bind_ip_all
    if ($LASTEXITCODE -ne 0) { throw 'Could not create disposable MongoDB.' }
    $containerCreated = $true
    $binding = (& docker port $containerName 27017/tcp).Trim()
    if ($LASTEXITCODE -ne 0 -or $binding -notmatch '^127\.0\.0\.1:(\d+)$') { throw 'Disposable MongoDB did not receive a loopback port.' }
    $mongoUrl = "mongodb://127.0.0.1:$($Matches[1])"
    $ready = $false
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        $null = & docker exec $containerName mongosh --quiet --eval 'quit(db.adminCommand({ping:1}).ok ? 0 : 1)' 2>$null
        if ($LASTEXITCODE -eq 0) { $ready = $true; break }
        Start-Sleep -Milliseconds 250
    }
    if (-not $ready) { throw 'Disposable MongoDB did not become ready.' }
    # Only the disposable fixture creates characters directly; game persistence always uses Characters HTTP.
    $fixture = @"
const d=db.getSiblingDB('$charactersDatabase');
d.characters.insertMany([
{_id:'$characterOne',SchemaVersion:1,AccountId:'$([Guid]::NewGuid().ToString('D'))',Name:'QuestOne',NormalizedName:'QUESTONE',Level:1,CreatedAt:new Date()},
{_id:'$characterTwo',SchemaVersion:1,AccountId:'$([Guid]::NewGuid().ToString('D'))',Name:'QuestTwo',NormalizedName:'QUESTTWO',Level:1,CreatedAt:new Date()}
]);
"@
    $null = & docker exec $containerName mongosh --quiet --eval $fixture
    if ($LASTEXITCODE -ne 0) { throw 'Could not seed disposable character fixtures.' }
    $ports = [Collections.Generic.HashSet[int]]::new()
    while ($ports.Count -lt 3) { $null = $ports.Add((Get-FreeTcpPort)) }
    $chosenPorts = @($ports)
    $contentUrl = "http://127.0.0.1:$($chosenPorts[0])"
    $charactersUrl = "http://127.0.0.1:$($chosenPorts[1])"
    $adminUrl = "http://127.0.0.1:$($chosenPorts[2])"
    $shared = @{ ASPNETCORE_ENVIRONMENT = 'Development'; DOTNET_ENVIRONMENT = 'Development'; Logging__LogLevel__Default = 'Warning' }
    $contentEnvironment = $shared.Clone()
    $contentEnvironment.ASPNETCORE_URLS = $contentUrl
    $contentEnvironment.Mongo__ConnectionString = $mongoUrl
    $contentEnvironment.Mongo__DatabaseName = $contentDatabase
    $contentEnvironment.ServiceAuthentication__Key = $contentServiceKey
    $contentEnvironment.ContentAdmin__Key = $contentAdminKey
    $charactersEnvironment = $shared.Clone()
    $charactersEnvironment.ASPNETCORE_URLS = $charactersUrl
    $charactersEnvironment.Mongo__ConnectionString = $mongoUrl
    $charactersEnvironment.Mongo__DatabaseName = $charactersDatabase
    $charactersEnvironment.ServiceAuthentication__Key = $charactersServiceKey
    $charactersEnvironment.Identity__BaseUrl = 'https://127.0.0.1:1'
    $charactersEnvironment.Identity__ServiceKey = $identityKey
    $adminEnvironment = $shared.Clone()
    $adminEnvironment.ASPNETCORE_URLS = $adminUrl
    $adminEnvironment.Admin__Password = $adminPassword
    $adminEnvironment.ContentApi__BaseUrl = $contentUrl
    $adminEnvironment.ContentApi__AdminKey = $contentAdminKey
    $contentWorker = Start-OwnedProcess 'content-api' $dotnet @((Join-Path $repositoryRoot 'backend/src/Arkya.Content.Api/bin/Debug/net10.0/Arkya.Content.Api.dll')) $contentEnvironment (Join-Path $repositoryRoot 'backend/src/Arkya.Content.Api') -CaptureOutput
    $charactersWorker = Start-OwnedProcess 'characters-api' $dotnet @((Join-Path $repositoryRoot 'backend/src/Arkya.Characters.Api/bin/Debug/net10.0/Arkya.Characters.Api.dll')) $charactersEnvironment (Join-Path $repositoryRoot 'backend/src/Arkya.Characters.Api') -CaptureOutput
    $adminWorker = Start-OwnedProcess 'admin-ui' $dotnet @((Join-Path $repositoryRoot 'backend/src/Arkya.Admin/bin/Debug/net10.0/Arkya.Admin.dll')) $adminEnvironment (Join-Path $repositoryRoot 'backend/src/Arkya.Admin') -CaptureOutput
    Wait-Healthy $contentUrl $contentWorker
    Wait-Healthy $charactersUrl $charactersWorker
    Wait-Healthy $adminUrl $adminWorker
    Submit-AdminForm '/Login' @{ Password = $adminPassword } '/Login'
    Assert-Check $true 'Admin login succeeded with cookie and antiforgery token'
    $original = Save-AndPublish 0 3
    Submit-AdminForm '/?handler=Publish' @{ Revision = 1 }
    $catalogs = @(Invoke-Json "$contentUrl/admin/content/catalogs" 'X-Arkya-Admin-Key' $contentAdminKey)
    Assert-Check ($catalogs.Count -eq 1 -and $catalogs[0].version -eq $original.version) 'Publication retries preserved the immutable catalog version'
    $snapshot = Invoke-Json "$contentUrl/internal/content/catalogs/$($original.version)" 'X-Arkya-Service-Key' $contentServiceKey
    Assert-Check ($snapshot.schemaVersion -eq 1 -and $snapshot.quests[0].requiredKills -eq 3) 'Dedicated service key can read the versioned published snapshot'
    $scenarios.Add((Run-QuestScenario $original.version $original.version 'initial-load'))
    $updated = Save-AndPublish 1 5
    Save-AdminPreview
    Assert-Check ($updated.version -ne $original.version) 'A changed draft creates a distinct published version'
    $oldSnapshot = Invoke-Json "$contentUrl/internal/content/catalogs/$($original.version)" 'X-Arkya-Service-Key' $contentServiceKey
    Assert-Check ($oldSnapshot.quests[0].requiredKills -eq 3) 'The first catalog remained immutable after publishing target five'
    $scenarios.Add((Run-QuestScenario $updated.version $original.version 'reload-persisted-progress'))

    $invalidDirectory = Join-Path $runDirectory 'unknown-catalog'
    New-Item -ItemType Directory -Path $invalidDirectory -Force | Out-Null
    $missingVersion = [Guid]::NewGuid().ToString('D')
    Set-QuestEnvironment $missingVersion
    $invalid = Start-OwnedProcess 'unknown-catalog-server' $resolvedServerExecutable @(
        '-arkyaPrototype', '-arkyaRole', 'server', '-arkyaArea', '0', '-arkyaRunId', ([Guid]::NewGuid().ToString('D')),
        '-arkyaResultDir', $invalidDirectory, '-arkyaStartUnixMs', ([DateTimeOffset]::UtcNow.AddSeconds(8).ToUnixTimeMilliseconds().ToString()),
        '-arkyaDuration', '15', '-arkyaQuestScenario', '-arkyaAutomated', '-batchmode', '-nographics', '-logFile', (Join-Path $invalidDirectory 'server-0.log')
    ) @{} ([IO.Path]::GetDirectoryName($resolvedServerExecutable))
    $listenObserved = $false
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(30)
    while (-not $invalid.Process.HasExited) {
        $endpoints = @(Get-NetUDPEndpoint -LocalPort 7777, 7790 -ErrorAction SilentlyContinue | Where-Object { $_.OwningProcess -eq $invalid.ProcessId })
        $listenObserved = $listenObserved -or $endpoints.Count -gt 0
        if ([DateTimeOffset]::UtcNow -gt $deadline) { throw 'Unknown-catalog server exceeded its startup deadline.' }
        Start-Sleep -Milliseconds 100
    }
    Assert-Check ($invalid.Process.ExitCode -eq 1) 'An unknown catalog makes the dedicated exit with code one'
    Assert-Check (-not $listenObserved) 'The invalid dedicated never opened its gameplay or peer listener'
    Assert-Check (Test-Path -LiteralPath (Join-Path $invalidDirectory 'startup-failure-0.json')) 'The invalid dedicated produced a startup failure diagnostic'
    Assert-Check (-not (Test-Path -LiteralPath (Join-Path $invalidDirectory 'server-0.json'))) 'The invalid dedicated never initialized gameplay simulation'
    $invalidCatalogResult = [pscustomobject]@{ MissingVersion = $missingVersion; ExitCode = $invalid.Process.ExitCode; ListenObserved = $listenObserved; ReportDirectory = $invalidDirectory }
    $passed = $true
} catch {
    $failure = $_.Exception.Message
    foreach ($secret in $secrets) { $failure = $failure.Replace($secret, '[redacted]') }
    throw $failure
} finally {
    foreach ($name in $environmentNames) { [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name], 'Process') }
    foreach ($worker in $owned) {
        try { Stop-OwnedProcess $worker } catch { Write-Warning "Could not clean up owned worker $($worker.Name)." }
        try {
            if ($null -ne $worker.OutputTask -and $worker.Process.HasExited) {
                $output = $worker.OutputTask.GetAwaiter().GetResult() + $worker.ErrorTask.GetAwaiter().GetResult()
                foreach ($secret in $secrets) { $output = $output.Replace($secret, '[redacted]') }
                $output | Set-Content -LiteralPath (Join-Path $runDirectory "$($worker.Name).log") -Encoding utf8
            }
        } catch { Write-Warning "Could not save output for owned worker $($worker.Name)." }
        finally { $worker.Process.Dispose() }
    }
    if ($containerCreated) { $null = & docker stop --time 3 $containerName }
    $browser.Dispose()
    $api.Dispose()
    [pscustomobject]@{
        Passed = $passed; Failure = $failure; CompletedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        RunDirectory = $runDirectory; CharacterOneId = $characterOne; CharacterTwoId = $characterTwo
        Scenarios = $scenarios.ToArray(); UnknownCatalog = $invalidCatalogResult; Checks = $checks.ToArray()
    } | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $runDirectory 'verification.json') -Encoding utf8
}
Write-Host "Quest content verification passed. Reports: $runDirectory"
