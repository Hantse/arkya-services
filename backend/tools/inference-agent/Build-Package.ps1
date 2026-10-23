[CmdletBinding()]
param(
    [string]$OutputDirectory = ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../artifacts/inference-agent-package'))),
    [string]$OllamaDirectory,
    [string]$PythonArchive,
    [string]$OllamaLicense,
    [string]$OllamaNotices
)
. (Join-Path $PSScriptRoot 'Agent.Common.ps1')
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$null = New-Item -ItemType Directory -Path $outputRoot -Force
$stage = Join-Path $outputRoot ('stage-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $stage
$dependencies = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $PSScriptRoot 'dependencies.json') | ConvertFrom-Json
$sourceFiles = @('agent.py','index.html','Agent.Common.ps1','Install-Agent.ps1','Start-Agent.ps1',
                 'Stop-Agent.ps1','Uninstall-Agent.ps1','dependencies.json','README.md','Install.cmd')
foreach ($name in $sourceFiles) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $stage }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../../src/Arkya.ServiceDefaults/InferenceModels.json') -Destination (Join-Path $stage 'models.json')
$provenance = [ordered]@{python='download-at-install'; ollama='download-at-install'}
if ($PythonArchive) {
    if ((Get-FileHash -LiteralPath $PythonArchive -Algorithm SHA256).Hash -ne $dependencies.python.sha256) { throw 'Python archive SHA256 mismatch.' }
    Expand-AgentRuntime -Archive $PythonArchive -Destination (Join-Path $stage 'runtime/python')
    [IO.File]::WriteAllText((Join-Path $stage 'runtime/python/python313._pth'), "python313.zip`n.`n../..`n", [Text.UTF8Encoding]::new($false))
    $provenance.python = "Official Python $($dependencies.python.version); archive SHA256 verified against python.org release metadata"
}
if ($OllamaDirectory) {
    $ollama = Join-Path $OllamaDirectory 'ollama.exe'
    $signature = Get-AuthenticodeSignature -LiteralPath $ollama
    if ($signature.Status -ne 'Valid') { throw 'Bundled Ollama executable must have a valid Authenticode signature.' }
    $versionStart = New-Object Diagnostics.ProcessStartInfo
    $versionStart.FileName = $ollama
    $versionStart.Arguments = '--version'
    $versionStart.UseShellExecute = $false
    $versionStart.CreateNoWindow = $true
    $versionStart.RedirectStandardOutput = $true
    $versionStart.RedirectStandardError = $true
    $versionProcess = [Diagnostics.Process]::Start($versionStart)
    $version = $versionProcess.StandardOutput.ReadToEnd() + $versionProcess.StandardError.ReadToEnd()
    $versionProcess.WaitForExit()
    $versionProcess.Dispose()
    if ($version -notmatch [regex]::Escape($dependencies.ollama.version)) { throw "Expected Ollama $($dependencies.ollama.version)." }
    $destination = Join-Path $stage 'runtime/ollama'
    $null = New-Item -ItemType Directory -Path (Join-Path $destination 'lib/ollama') -Force
    Copy-Item -LiteralPath $ollama -Destination $destination
    foreach ($dll in Get-ChildItem -LiteralPath (Join-Path $OllamaDirectory 'lib/ollama') -Filter '*.dll' -File) {
        Copy-Item -LiteralPath $dll.FullName -Destination (Join-Path $destination 'lib/ollama')
    }
    if (-not (Test-Path -LiteralPath (Join-Path $destination 'lib/ollama/ggml-base.dll')) -or
        -not (Test-Path -LiteralPath (Join-Path $destination 'lib/ollama/ggml-cpu-x64.dll'))) { throw 'CPU runtime DLLs are missing.' }
    $provenance.ollama = "Existing official signed Windows installation $($dependencies.ollama.version); CPU-only DLL subset; signer $($signature.SignerCertificate.Subject)"
    $licenses = Join-Path $stage 'licenses'
    $null = New-Item -ItemType Directory -Path $licenses -Force
    if ($OllamaLicense) { Copy-Item -LiteralPath $OllamaLicense -Destination (Join-Path $licenses 'OLLAMA-LICENSE.txt') }
    else { Invoke-WebRequest -UseBasicParsing -Uri "https://raw.githubusercontent.com/ollama/ollama/v$($dependencies.ollama.version)/LICENSE" -OutFile (Join-Path $licenses 'OLLAMA-LICENSE.txt') }
    if ($OllamaNotices) { Copy-Item -LiteralPath $OllamaNotices -Destination (Join-Path $licenses 'OLLAMA-NOTICES.txt') }
    else {
        $notices = foreach ($licensePath in @('llama/llama.cpp/LICENSE','ml/backend/ggml/ggml/LICENSE')) {
            $licenseUrl = "https://raw.githubusercontent.com/ollama/ollama/v$($dependencies.ollama.version)/$licensePath"
            $licenseText = (Invoke-WebRequest -UseBasicParsing -Uri $licenseUrl).Content
            "$licensePath`n$licenseText`n"
        }
        [IO.File]::WriteAllText((Join-Path $licenses 'OLLAMA-NOTICES.txt'), ($notices -join "`n"), [Text.UTF8Encoding]::new($false))
    }
}
$files = foreach ($file in Get-ChildItem -LiteralPath $stage -File -Recurse) {
    @{path=$file.FullName.Substring($stage.Length + 1).Replace('\','/'); sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant(); bytes=$file.Length}
}
$manifest = @{schemaVersion=1; version='1.2.0'; createdUtc=[DateTimeOffset]::UtcNow.ToString('O'); provenance=$provenance; files=@($files)}
[IO.File]::WriteAllText((Join-Path $stage 'package-manifest.json'), ($manifest | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
$zip = Join-Path $outputRoot 'Arkya-Inference-Agent-Windows-x64.zip'
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal -Force
$digest = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText(($zip + '.sha256'), "$digest  $([IO.Path]::GetFileName($zip))`n")
Write-Output "Package: $zip"
Write-Output "SHA256: $digest"
Write-Output "Staging vérifié pour tests: $stage"
