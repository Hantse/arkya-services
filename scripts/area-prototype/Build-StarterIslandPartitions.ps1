#requires -Version 7.4
[CmdletBinding()]
param(
    [string]$ArtifactRoot = '',
    [string]$UnityExecutable = 'D:/Unity/6000.6.6f1/Editor/Unity.exe',
    [switch]$PrepareOnly,
    [switch]$RefreshSource,
    [ValidateSet('Both', 'Client', 'Server')][string]$Role = 'Both'
)
$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$source = Join-Path $workspace 'Unity3D/Arkya'
if (!$ArtifactRoot) {
    $ArtifactRoot = Join-Path $workspace ('artifacts/starter-island-partitions/build-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
}
$ArtifactRoot = [IO.Path]::GetFullPath($ArtifactRoot)
if (!$ArtifactRoot.StartsWith((Join-Path $workspace 'artifacts') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Build snapshots must remain inside this workspace artifacts directory.'
}
if (!(Test-Path -LiteralPath $UnityExecutable -PathType Leaf)) { throw 'The configured Unity executable is missing.' }
function Copy-Tree([string]$From, [string]$To) {
    if (!(Test-Path -LiteralPath $From -PathType Container)) { throw "Source folder is missing: $From" }
    $null = New-Item -ItemType Directory -Path $To -Force
    $copyArguments = @($From, $To, '/E', '/R:1', '/W:1', '/MT:8', '/NFL', '/NDL', '/NJH', '/NJS', '/NP', '/XJ')
    if ([IO.Path]::GetFileName($From) -eq 'Packages') { $copyArguments += @('/XD', (Join-Path $From 'Aura')) }
    $output = & robocopy @copyArguments
    if ($LASTEXITCODE -ge 8) { throw "Snapshot copy failed: $From" }
}
function Assert-ProjectClosed([string]$Project) {
    $lockPath = Join-Path $Project 'Temp/UnityLockfile'
    if (!(Test-Path -LiteralPath $lockPath)) { return }
    try {
        $stream = [IO.File]::Open($lockPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
        $stream.Dispose()
    }
    catch { throw 'An isolated build checkout is already open.' }
}
function Prepare-Project([string]$Project, [string]$CapturedSource) {
    if (Test-Path -LiteralPath $Project) { return }
    foreach ($folder in @('Assets', 'Packages', 'ProjectSettings')) {
        Copy-Tree (Join-Path $CapturedSource $folder) (Join-Path $Project $folder)
    }
    Configure-GamePackages $Project
}
function Configure-GamePackages([string]$Project) {
    $path = Join-Path $Project 'Packages/manifest.json'
    $manifest = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable
    $required = @('com.unity.entities', 'com.unity.netcode', 'com.unity.transport', 'com.unity.inputsystem',
        'com.unity.render-pipelines.universal', 'com.unity.ugui', 'com.unity.cloud.gltfast',
        'com.unity.pipeline', 'com.unity.test-framework')
    foreach ($dependency in @($manifest.dependencies.Keys)) {
        if ($dependency -notin $required -and !$dependency.StartsWith('com.unity.modules.')) {
            $manifest.dependencies.Remove($dependency)
        }
    }
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $path -Encoding utf8NoBOM
}
$null = New-Item -ItemType Directory -Path $ArtifactRoot -Force
$clientProject = Join-Path $ArtifactRoot 'project-client'
$serverProject = Join-Path $ArtifactRoot 'project-server'
Prepare-Project $clientProject $source
# Both roles use the same captured source even when the working tree changes during a build.
Prepare-Project $serverProject $clientProject
if ($RefreshSource) {
    foreach ($project in @($clientProject, $serverProject)) {
        Assert-ProjectClosed $project
    }
    foreach ($folder in @('Assets', 'Packages', 'ProjectSettings')) {
        Copy-Tree (Join-Path $source $folder) (Join-Path $clientProject $folder)
        Copy-Tree (Join-Path $clientProject $folder) (Join-Path $serverProject $folder)
    }
    Configure-GamePackages $clientProject
    Configure-GamePackages $serverProject
}
if ($RefreshSource -or !(Test-Path -LiteralPath (Join-Path $ArtifactRoot 'source.json'))) {
    $manifest = [ordered]@{
        capturedAt = [DateTimeOffset]::UtcNow.ToString('O'); sourceProject = $source
        sourceCommit = (& git -C $source rev-parse HEAD).Trim()
        clientProject = $clientProject; serverProject = $serverProject
        runtimeHashes = @()
    }
    $manifest.runtimeHashes = @(Get-ChildItem -LiteralPath (Join-Path $clientProject 'Assets/Arkya/Characters/Runtime') -Filter '*.cs' -Recurse |
        ForEach-Object { @{ path = [IO.Path]::GetRelativePath($clientProject, $_.FullName); sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash } })
    $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $ArtifactRoot 'source.json')
}
if ($PrepareOnly) { Write-Output (Join-Path $ArtifactRoot 'source.json'); return }
$roles = if ($Role -eq 'Both') { @('Server', 'Client') } else { @($Role) }
foreach ($selected in $roles) {
    $project = if ($selected -eq 'Server') { $serverProject } else { $clientProject }
    Assert-ProjectClosed $project
    $subtarget = if ($selected -eq 'Server') { 'Server' } else { 'Player' }
    $method = if ($selected -eq 'Server') { 'Arkya.Characters.Editor.CharacterWebBetaBuilder.BuildWindowsServer' } else { 'Arkya.Characters.Editor.CharacterCreationBuilder.BuildPlayer' }
    # Settle a fresh checkout's import before invoking builders that require compilation to be idle.
    $importLog = Join-Path $ArtifactRoot ("$($selected.ToLowerInvariant())-import-" + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.log')
    $importStart = [Diagnostics.ProcessStartInfo]::new($UnityExecutable)
    $importStart.WorkingDirectory = $project; $importStart.UseShellExecute = $false; $importStart.CreateNoWindow = $true
    $importStart.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    foreach ($argument in @('-batchmode', '-nographics', '-quit', '-job-worker-count', '4', '-projectPath', $project,
        '-buildTarget', 'Win64', '-standaloneBuildSubtarget', $subtarget, '-logFile', $importLog)) {
        $importStart.ArgumentList.Add($argument)
    }
    $importStart.Environment['DOTNET_PROCESSOR_COUNT'] = '4'
    $importProcess = [Diagnostics.Process]::Start($importStart)
    Write-Output "Unity $selected import PID $($importProcess.Id); $importLog"
    while (!$importProcess.WaitForExit(1000)) { }
    if ($importProcess.ExitCode -ne 0) { throw "Unity $selected import failed; inspect its log." }
    $log = Join-Path $ArtifactRoot "$($selected.ToLowerInvariant())-build.log"
    if (Test-Path -LiteralPath $log) {
        Copy-Item -LiteralPath $log -Destination ($log + '.previous-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 6))
    }
    $start = [Diagnostics.ProcessStartInfo]::new($UnityExecutable)
    $start.WorkingDirectory = $project; $start.UseShellExecute = $false; $start.CreateNoWindow = $true
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    foreach ($argument in @('-batchmode', '-nographics', '-quit', '-job-worker-count', '4', '-projectPath', $project,
        '-buildTarget', 'Win64', '-standaloneBuildSubtarget', $subtarget, '-executeMethod', $method, '-logFile', $log)) {
        $start.ArgumentList.Add($argument)
    }
    $start.Environment['ARKYA_CHARACTER_BUILD'] = Join-Path $ArtifactRoot 'client/Arkya.exe'
    $start.Environment['DOTNET_PROCESSOR_COUNT'] = '4'
    $process = [Diagnostics.Process]::Start($start)
    @{ role = $selected; id = $process.Id; project = $project; startedAt = $process.StartTime.ToUniversalTime().ToString('O'); log = $log } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $ArtifactRoot "$($selected.ToLowerInvariant())-process.json")
    Write-Output "Unity $selected build PID $($process.Id); $log"
    while (!$process.WaitForExit(1000)) { }
    if ($process.ExitCode -ne 0) { throw "Unity $selected build failed; inspect its log." }
    $marker = if ($selected -eq 'Server') { 'ARKYA_WEB_BETA_DEDICATED_BUILD_SUCCESS' } else { 'ARKYA_CHARACTER_BUILD_PASSED' }
    if (!(Select-String -LiteralPath $log -SimpleMatch $marker -Quiet)) { throw "The $selected build did not produce its success marker." }
}
@{
    clientExecutable = Join-Path $ArtifactRoot 'client/Arkya.exe'
    serverExecutable = Join-Path $serverProject 'Builds/WebBetaServerWindows/ArkyaServer.exe'
    completedAt = [DateTimeOffset]::UtcNow.ToString('O')
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $ArtifactRoot 'build.json')
Write-Output (Join-Path $ArtifactRoot 'build.json')
