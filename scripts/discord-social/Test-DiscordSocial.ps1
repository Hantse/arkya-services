[CmdletBinding()]
param([string]$UnityPath = 'D:/Unity/6000.6.6f1/Editor/Unity.exe')

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$sourceProject = Join-Path $repository 'Unity3D/Arkya'
$artifactRoot = Join-Path $repository 'artifacts/discord-social-sdk'
$validationProject = Join-Path $artifactRoot 'validation-project'
if (-not (Test-Path -LiteralPath $UnityPath -PathType Leaf)) { throw 'Unity Editor executable not found.' }
foreach ($candidate in Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'") {
    if ($candidate.CommandLine -and $candidate.CommandLine.Contains($validationProject)) {
        throw 'The isolated Discord validation project is already open. Close that instance before running these tests.'
    }
}

New-Item -ItemType Directory -Path (Join-Path $validationProject 'Assets/Arkya'), (Join-Path $validationProject 'Packages'),
    (Join-Path $validationProject 'ProjectSettings') -Force | Out-Null
foreach ($folder in @('Characters', 'Social')) {
    Copy-Item -LiteralPath (Join-Path $sourceProject "Assets/Arkya/$folder") -Destination (Join-Path $validationProject 'Assets/Arkya') -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $sourceProject "Assets/Arkya/$folder.meta") -Destination (Join-Path $validationProject 'Assets/Arkya') -Force
}
Copy-Item -LiteralPath (Join-Path $sourceProject 'Assets/Arkya.meta') -Destination (Join-Path $validationProject 'Assets') -Force
Copy-Item -LiteralPath (Join-Path $sourceProject 'Assets/Settings') -Destination (Join-Path $validationProject 'Assets') -Recurse -Force
Copy-Item -LiteralPath (Join-Path $sourceProject 'Assets/Settings.meta') -Destination (Join-Path $validationProject 'Assets') -Force
Copy-Item -LiteralPath (Join-Path $sourceProject 'Packages/com.discord.partnersdk') -Destination (Join-Path $validationProject 'Packages') -Recurse -Force
foreach ($setting in @('ProjectVersion.txt', 'ProjectSettings.asset', 'GraphicsSettings.asset', 'QualitySettings.asset', 'TagManager.asset')) {
    Copy-Item -LiteralPath (Join-Path $sourceProject "ProjectSettings/$setting") -Destination (Join-Path $validationProject 'ProjectSettings') -Force
}
$sourceManifest = Get-Content -LiteralPath (Join-Path $sourceProject 'Packages/manifest.json') -Raw | ConvertFrom-Json
$sourceLock = Get-Content -LiteralPath (Join-Path $sourceProject 'Packages/packages-lock.json') -Raw | ConvertFrom-Json
$required = @('com.unity.entities', 'com.unity.netcode', 'com.unity.transport', 'com.unity.inputsystem', 'com.unity.render-pipelines.universal', 'com.unity.ugui')
$dependencies = [ordered]@{}
foreach ($dependency in $sourceManifest.dependencies.PSObject.Properties) {
    if ($dependency.Name -in $required -or $dependency.Name.StartsWith('com.unity.modules.')) {
        $dependencies[$dependency.Name] = $dependency.Value
    }
}
$dependencies['com.unity.test-framework'] = $sourceLock.dependencies.'com.unity.test-framework'.version
@{ dependencies = $dependencies } | ConvertTo-Json -Depth 4 |
    Set-Content -LiteralPath (Join-Path $validationProject 'Packages/manifest.json') -Encoding utf8

$resultsPath = Join-Path $artifactRoot 'unity-tests.xml'
$logPath = Join-Path $artifactRoot 'unity-tests.log'
$arguments = @('-batchmode', '-projectPath', $validationProject, '-runTests', '-testPlatform', 'EditMode',
    '-testFilter', 'Arkya.Social.Tests;Arkya.Characters.Tests.DiscordSocialPanelRuntimeTests', '-testResults', $resultsPath, '-logFile', $logPath)
$quotedArguments = $arguments | ForEach-Object { '"' + $_.Replace('"', '\"') + '"' }
$process = Start-Process -FilePath $UnityPath -ArgumentList ($quotedArguments -join ' ') -WindowStyle Hidden -PassThru
Write-Output "Unity validation started (PID $($process.Id)). Results: $resultsPath"
$process.WaitForExit()
if ($process.ExitCode -ne 0) { throw "Unity tests exited with code $($process.ExitCode). See $logPath" }
if (-not (Test-Path -LiteralPath $resultsPath)) { throw 'Unity did not produce a test result.' }
[xml]$results = Get-Content -LiteralPath $resultsPath -Raw
$run = $results.'test-run'
Write-Output "Tests: $($run.total); passed: $($run.passed); failed: $($run.failed); skipped: $($run.skipped)"
if ($run.result -ne 'Passed' -or [int]$run.total -eq 0) { throw "Unity test result: $($run.result)" }
