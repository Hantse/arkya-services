[CmdletBinding()]
param(
    [string]$UnityPath = 'D:/Unity/6000.6.6f1/Editor/Unity.exe',
    [switch]$FullProjectPackages,
    [switch]$CleanBuild,
    [string]$ArtifactRoot,
    [switch]$LowMemory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$sourceProject = Join-Path $repository 'Unity3D/Arkya'
$copySourceProject = $sourceProject
if ([string]::IsNullOrWhiteSpace($ArtifactRoot)) { $ArtifactRoot = Join-Path $repository 'artifacts/area-prototype' }
$artifactRoot = [IO.Path]::GetFullPath($ArtifactRoot)

function ConvertTo-NativeArgument([string]$Value) {
    if ($Value.Length -gt 0 -and $Value -notmatch '[\s"]') { return $Value }
    $escaped = [regex]::Replace($Value, '(\\*)"', '$1$1\"')
    $escaped = [regex]::Replace($escaped, '(\\+)$', '$1$1')
    return '"' + $escaped + '"'
}

$prototypeExecutables = @(
    [IO.Path]::GetFullPath((Join-Path $artifactRoot 'build/Arkya.AreaPrototype.exe')),
    [IO.Path]::GetFullPath((Join-Path $artifactRoot 'build/server/Arkya.AreaPrototype.Server.exe'))
)
$lockingProcesses = @(foreach ($candidate in Get-Process -ErrorAction SilentlyContinue) {
    try {
        if ($candidate.Path -in $prototypeExecutables) {
            [pscustomobject]@{ ProcessId = $candidate.Id; ExecutablePath = $candidate.Path }
        }
    } catch {
        # Unrelated protected or already exited processes do not hold these build outputs.
        continue
    }
})
if ($lockingProcesses.Count -gt 0) {
    $details = @($lockingProcesses | ForEach-Object { "PID $($_.ProcessId): $($_.ExecutablePath)" }) -join '; '
    throw "Cannot rebuild while prototype players or servers are running ($details). Stop the previous run before rebuilding; its loaded DLLs may still be locked."
}

if (-not (Test-Path -LiteralPath $UnityPath -PathType Leaf)) { throw "Unity executable missing: $UnityPath" }
function Initialize-ValidationProject([string]$validationProject) {
New-Item -ItemType Directory -Path (Join-Path $validationProject 'Assets/Arkya'), (Join-Path $validationProject 'Packages'),
    (Join-Path $validationProject 'ProjectSettings') -Force | Out-Null

# Compile the project scripts without locking the user's open art project.
Copy-Item -LiteralPath (Join-Path $copySourceProject 'Assets/Arkya/AreaPrototype') -Destination (Join-Path $validationProject 'Assets/Arkya') -Recurse -Force
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $copySourceProject 'Assets') -Recurse -File |
    Where-Object { $_.Extension -in @('.cs', '.asmdef', '.asmref') }) {
    $relative = [IO.Path]::GetRelativePath($copySourceProject, $file.FullName)
    $destination = Join-Path $validationProject $relative
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($destination)) -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
    if (Test-Path -LiteralPath ($file.FullName + '.meta')) {
        Copy-Item -LiteralPath ($file.FullName + '.meta') -Destination ($destination + '.meta') -Force
    }
}
Copy-Item -LiteralPath (Join-Path $copySourceProject 'Assets/Settings') -Destination (Join-Path $validationProject 'Assets') -Recurse -Force
Copy-Item -LiteralPath (Join-Path $copySourceProject 'Assets/Settings.meta') -Destination (Join-Path $validationProject 'Assets/Settings.meta') -Force
$sourceManifestPath = Join-Path $copySourceProject 'Packages/manifest.json'
if ($FullProjectPackages) {
    Copy-Item -LiteralPath $sourceManifestPath -Destination (Join-Path $validationProject 'Packages/manifest.json') -Force
} else {
    $sourceManifest = Get-Content -LiteralPath $sourceManifestPath -Raw | ConvertFrom-Json
    $required = @('com.unity.entities', 'com.unity.netcode', 'com.unity.transport', 'com.unity.inputsystem',
        'com.unity.render-pipelines.universal', 'com.unity.ugui', 'com.unity.test-framework')
    $dependencies = [ordered]@{}
    foreach ($dependency in $sourceManifest.dependencies.PSObject.Properties) {
        if ($dependency.Name -in $required -or $dependency.Name.StartsWith('com.unity.modules.')) {
            $dependencies[$dependency.Name] = $dependency.Value
        }
    }
    @{ dependencies = $dependencies } | ConvertTo-Json -Depth 4 |
        Set-Content -LiteralPath (Join-Path $validationProject 'Packages/manifest.json') -Encoding utf8
}
foreach ($setting in @('ProjectVersion.txt', 'ProjectSettings.asset', 'GraphicsSettings.asset', 'QualitySettings.asset', 'TagManager.asset')) {
    Copy-Item -LiteralPath (Join-Path $copySourceProject "ProjectSettings/$setting") -Destination (Join-Path $validationProject 'ProjectSettings') -Force
}
}

$responses = @{}
# Entities derives the player type from Editor compilation defines, so each role needs a fresh process.
foreach ($role in @('client', 'server')) {
    $packageScope = if ($FullProjectPackages) { 'full' } else { 'prototype' }
    $validationProject = Join-Path $artifactRoot "validation-$packageScope-$role"
    Initialize-ValidationProject $validationProject
    if ($role -eq 'client') { $copySourceProject = $validationProject }
    $subtarget = if ($role -eq 'server') { 'Server' } else { 'Player' }
    $logPath = Join-Path $artifactRoot "unity-build-$role.log"
    $arguments = @('-batchmode', '-nographics', '-job-worker-count', '4', '-projectPath', $validationProject,
        '-standaloneBuildSubtarget', $subtarget, '-arkyaBuildRole', $role, '-executeMethod',
        'Arkya.AreaPrototype.Editor.AreaPrototypeEditorMailbox.BuildFromCommandLine', '-arkyaArtifactRoot', $artifactRoot, '-logFile', $logPath)
    if ($LowMemory) { $arguments += '-diag-debug-shader-compiler' }
    if ($CleanBuild) { $arguments += '-arkyaCleanBuild' }
    $cacheDirectory = Join-Path $artifactRoot 'bee-cache'
    $temporaryDirectory = Join-Path $artifactRoot 'temp'
    New-Item -ItemType Directory -Path $cacheDirectory, $temporaryDirectory -Force | Out-Null
    $buildEnvironment = @{
        BEE_BUILD_THREADS = '2'
        BEE_CACHE_DIRECTORY = $cacheDirectory
        DOTNET_PROCESSOR_COUNT = '2'
        TEMP = $temporaryDirectory
        TMP = $temporaryDirectory
    }
    $previousEnvironment = @{}
    try {
        foreach ($name in $buildEnvironment.Keys) {
            $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
            [Environment]::SetEnvironmentVariable($name, $buildEnvironment[$name], 'Process')
        }
        $process = Start-Process -FilePath $UnityPath -ArgumentList (($arguments | ForEach-Object { ConvertTo-NativeArgument $_ }) -join ' ') -WindowStyle Hidden -PassThru
    } finally {
        foreach ($name in $previousEnvironment.Keys) {
            [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name], 'Process')
        }
    }
    Write-Host "Unity $role compile/build PID $($process.Id); log: $logPath"
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw "Unity $role build exited with code $($process.ExitCode). See $logPath" }
    $roleResponse = Get-Content -LiteralPath (Join-Path $artifactRoot 'editor-response.json') -Raw | ConvertFrom-Json
    $roleResponse | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $artifactRoot "$role-build-response.json") -Encoding utf8
    if (-not $roleResponse.success -or -not $roleResponse.cleanBuild -or $roleResponse.role -ne $role) {
        throw "Unity $role build failed with exit $($process.ExitCode): $($roleResponse.message). See $logPath"
    }
    $responses[$role] = $roleResponse
}
$response = [pscustomobject]@{
    success = $responses.client.success -and $responses.server.success
    cleanBuild = $responses.client.cleanBuild -and $responses.server.cleanBuild
    message = 'Client: ' + $responses.client.message + '; Dedicated server: ' + $responses.server.message
    unityVersion = $responses.client.unityVersion
    completedAtUtc = $responses.server.completedAtUtc
    outputPath = $responses.client.outputPath
    serverOutputPath = $responses.server.serverOutputPath
    buildErrors = $responses.client.buildErrors + $responses.server.buildErrors
    buildWarnings = $responses.client.buildWarnings + $responses.server.buildWarnings
    packageScope = $packageScope
}
$response | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $artifactRoot 'build-response.json') -Encoding utf8

# Unity owns generated metadata and the new prototype scene; keep existing source assets intact.
$validatedAssets = Join-Path $validationProject 'Assets/Arkya/AreaPrototype'
$sourceAssets = Join-Path $sourceProject 'Assets/Arkya/AreaPrototype'
foreach ($file in Get-ChildItem -LiteralPath $validatedAssets -Recurse -File) {
    if ($file.Extension -notin @('.meta', '.unity', '.mat')) { continue }
    $destination = Join-Path $sourceAssets ([IO.Path]::GetRelativePath($validatedAssets, $file.FullName))
    if (Test-Path -LiteralPath $destination) { continue }
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($destination)) -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $destination
}
foreach ($folderMeta in @('Assets/Arkya/AreaPrototype.meta')) {
    if (-not (Test-Path -LiteralPath (Join-Path $sourceProject $folderMeta))) {
        Copy-Item -LiteralPath (Join-Path $validationProject $folderMeta) -Destination (Join-Path $sourceProject $folderMeta)
    }
}
if ($FullProjectPackages) {
    Copy-Item -LiteralPath (Join-Path $validationProject 'Packages/packages-lock.json') -Destination (Join-Path $sourceProject 'Packages/packages-lock.json') -Force
}
Write-Host "Built prototype: $($response.outputPath)"
return $response
