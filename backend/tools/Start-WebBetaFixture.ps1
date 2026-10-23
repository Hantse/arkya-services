#requires -Version 7.4
[CmdletBinding()]
param(
    [ValidateSet('Start', 'Restart', 'Tickets', 'VerifyState', 'VerifyRevocation', 'Stop')][string]$Action = 'Start',
    [string]$Directory = '',
    [ValidateSet('Debug', 'Release')][string]$BuildConfiguration = 'Debug',
    [ValidateSet(1, 4)][int]$ServerCount = 1
)
$ErrorActionPreference = 'Stop'
$backend = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$tool = Join-Path $backend 'tools/Arkya.WebBeta.Fixture/Arkya.WebBeta.Fixture.csproj'
if (!$Directory) {
    if ($Action -ne 'Start') { throw 'Directory is required for actions on an existing fixture.' }
    $Directory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) ('Arkya/WebBetaTests/' + [guid]::NewGuid().ToString('N'))
}
$Directory = [IO.Path]::GetFullPath($Directory)
$fixturePath = Join-Path $Directory 'fixture.json'
$processPath = Join-Path $Directory 'processes.json'

function Invoke-FixtureTool([string]$command) {
    $result = & dotnet run --project $tool --configuration $BuildConfiguration --no-build -- $command $Directory
    if ($LASTEXITCODE -ne 0) { throw "Fixture $command failed; no credentials are included in this error." }
    return $result
}

function Stop-FixtureProcesses {
    if (Test-Path -LiteralPath $processPath) {
        $processes = Get-Content -LiteralPath $processPath -Raw | ConvertFrom-Json
        foreach ($record in $processes) {
            $process = Get-Process -Id $record.id -ErrorAction SilentlyContinue
            if ($process -and $process.StartTime.ToUniversalTime() -eq ([DateTimeOffset]$record.startedAt).UtcDateTime) {
                Stop-Process -Id $process.Id
                $process.WaitForExit(5000) | Out-Null
            }
        }
    }
}

if ($Action -in @('Start', 'Restart')) {
    if ($Action -eq 'Start' -and !$env:ARKYA_TEST_MONGO) { throw 'Set ARKYA_TEST_MONGO to an isolated real MongoDB URI.' }
    if ($Action -eq 'Start' -and (Test-Path -LiteralPath $fixturePath)) { throw 'A fixture already exists at this path.' }
    if ($Action -eq 'Restart' -and !(Test-Path -LiteralPath $fixturePath)) { throw 'No fixture exists to restart.' }
    if ($Action -eq 'Restart') { Stop-FixtureProcesses }
    New-Item -ItemType Directory -Path $Directory -Force | Out-Null
    if ($IsWindows -and $Action -eq 'Start') {
        $acl = Get-Acl -LiteralPath $Directory
        $acl.SetAccessRuleProtection($true, $false)
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent().User
        $rule = [Security.AccessControl.FileSystemAccessRule]::new($identity, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
        $acl.SetAccessRule($rule); Set-Acl -LiteralPath $Directory -AclObject $acl
    }
    foreach ($project in @($tool, (Join-Path $backend 'src/Arkya.Identity.Api/Arkya.Identity.Api.csproj'),
        (Join-Path $backend 'src/Arkya.Characters.Api/Arkya.Characters.Api.csproj'), (Join-Path $backend 'src/Arkya.Realms.Api/Arkya.Realms.Api.csproj'))) {
        & dotnet build $project --configuration $BuildConfiguration --verbosity quiet
        if ($LASTEXITCODE -ne 0) { throw 'Fixture service build failed.' }
    }
    if ($Action -eq 'Start') {
        $previousCount = $env:ARKYA_TEST_SERVER_COUNT
        try { $env:ARKYA_TEST_SERVER_COUNT = [string]$ServerCount; $null = Invoke-FixtureTool 'prepare' }
        finally { $env:ARKYA_TEST_SERVER_COUNT = $previousCount }
    }
    $fixture = Get-Content -LiteralPath $fixturePath -Raw | ConvertFrom-Json -AsHashtable
    $launched = @()
    foreach ($service in $fixture.services.Keys) {
        $folder = Join-Path $backend "src/$service"
        $dll = Join-Path $folder "bin/$BuildConfiguration/net10.0/$service.dll"
        $parameters = @{
            FilePath = 'dotnet'; ArgumentList = @('"' + $dll + '"'); WorkingDirectory = $folder
            Environment = $fixture.services[$service]; PassThru = $true
            RedirectStandardOutput = (Join-Path $Directory "$service.out.log")
            RedirectStandardError = (Join-Path $Directory "$service.err.log")
        }
        if ($IsWindows) { $parameters.WindowStyle = 'Hidden' }
        $process = Start-Process @parameters
        $launched += @{ id = $process.Id; service = $service; startedAt = $process.StartTime.ToUniversalTime().ToString('O') }
        ConvertTo-Json -InputObject $launched | Set-Content -LiteralPath $processPath
    }
    foreach ($url in @(($fixture.identityBaseUrl + '/health'), ($fixture.charactersBaseUrl + '/health'), ($fixture.realmsBaseUrl + '/health/ready'))) {
        $ready = $false
        for ($attempt = 0; $attempt -lt 40; $attempt++) {
            try { $null = Invoke-RestMethod -Uri $url -TimeoutSec 2; $ready = $true; break }
            catch { Start-Sleep -Milliseconds 250 }
        }
        if (!$ready) { throw 'A fixture API did not become ready. Inspect the private fixture logs, then run Stop to clean up.' }
    }
    if ($Action -eq 'Start') { $null = Invoke-FixtureTool 'characters' }
    Write-Output "Real service fixture ready: $fixturePath"
    Write-Output 'OAuth is not exercised: this isolated test database contains an explicitly seeded test account/session.'
}
elseif ($Action -eq 'Tickets') {
    $output = Invoke-FixtureTool 'tickets'
    $env:ARKYA_WEB_BETA_TICKETS_FILE = Join-Path $Directory 'tickets.json'
    Write-Output $output
}
elseif ($Action -eq 'VerifyState') {
    Invoke-FixtureTool 'verify-state'
}
elseif ($Action -eq 'VerifyRevocation') {
    Invoke-FixtureTool 'verify-revocation'
}
else {
    Stop-FixtureProcesses
    $null = Invoke-FixtureTool 'cleanup'
    if (Test-Path -LiteralPath $processPath) { Remove-Item -LiteralPath $processPath }
    Write-Output 'Fixture APIs stopped, isolated test databases removed, and session/ticket files deleted.'
}
