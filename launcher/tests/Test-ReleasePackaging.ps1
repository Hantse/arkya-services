$ErrorActionPreference = 'Stop'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('arkya-package-test-' + [guid]::NewGuid().ToString('N'))
$buildRoot = Join-Path $testRoot 'synthetic-build'
$outputRoot = Join-Path $testRoot 'release'
$publisher = Join-Path $PSScriptRoot '../scripts/New-GameRelease.ps1'
New-Item -ItemType Directory -Path (Join-Path $buildRoot 'Arkya_Data'), (Join-Path $buildRoot 'Arkya_BackUpThisFolder_ButDontShipItWithYourGame') -Force | Out-Null
try {
    foreach ($name in @('Arkya.exe', 'UnityPlayer.dll', 'Arkya_Data/data.bin', 'debug.pdb', 'Arkya_BackUpThisFolder_ButDontShipItWithYourGame/debug.txt')) {
        [IO.File]::WriteAllText((Join-Path $buildRoot $name), 'Synthetic packaging fixture. Never execute.')
    }
    $result = & $publisher -BuildDirectory $buildRoot -Version 'fixture-1' -PackageUrl 'https://example.test/fixture.zip' -OutputDirectory $outputRoot
    $manifest = Get-Content -LiteralPath $result.Manifest -Raw | ConvertFrom-Json
    if ($manifest.PSObject.Properties.Name -contains 'launcherSessionProtocol') { throw 'Legacy release unexpectedly requires a session handoff.' }
    if ($manifest.sha256 -ne (Get-FileHash -LiteralPath $result.Archive -Algorithm SHA256).Hash.ToLowerInvariant()) { throw 'Package hash mismatch.' }
    if ($manifest.packageSize -ne (Get-Item -LiteralPath $result.Archive).Length) { throw 'Package size mismatch.' }
    $archive = [IO.Compression.ZipFile]::OpenRead($result.Archive)
    try {
        $entries = @($archive.Entries)
        if ($entries.Count -ne 3) { throw 'Debug files were included or payload files are missing.' }
        if (($entries | Measure-Object -Property Length -Sum).Sum -ne $manifest.unpackedSize) { throw 'Expanded size mismatch.' }
        if ($entries.FullName -notcontains 'Arkya_Data/data.bin') { throw 'Build directory layout was changed.' }
    } finally { $archive.Dispose() }
    $refusedOverwrite = $false
    try { & $publisher -BuildDirectory $buildRoot -Version 'fixture-1' -PackageUrl 'https://example.test/fixture.zip' -OutputDirectory $outputRoot | Out-Null }
    catch { $refusedOverwrite = $true }
    if (-not $refusedOverwrite) { throw 'Publisher overwrote an existing release.' }
    $refusedNestedOutput = $false
    try { & $publisher -BuildDirectory $buildRoot -Version 'fixture-2' -PackageUrl 'https://example.test/fixture.zip' -OutputDirectory (Join-Path $buildRoot 'release') | Out-Null }
    catch { $refusedNestedOutput = $true }
    if (-not $refusedNestedOutput) { throw 'Publisher accepted an output inside its input.' }
    $sessionRelease = & $publisher -BuildDirectory $buildRoot -Version 'fixture-session-1' -PackageUrl 'https://example.test/fixture-session.zip' -OutputDirectory (Join-Path $testRoot 'session-release') -LauncherSessionProtocol 1
    $sessionManifest = Get-Content -LiteralPath $sessionRelease.Manifest -Raw | ConvertFrom-Json
    if ($sessionManifest.launcherSessionProtocol -ne 1) { throw 'Session handoff capability was not included.' }
    $refusedUnknownProtocol = $false
    try { & $publisher -BuildDirectory $buildRoot -Version 'fixture-session-2' -PackageUrl 'https://example.test/fixture-session-2.zip' -OutputDirectory (Join-Path $testRoot 'invalid-session-release') -LauncherSessionProtocol 2 | Out-Null }
    catch { $refusedUnknownProtocol = $true }
    if (-not $refusedUnknownProtocol) { throw 'Publisher accepted an unknown session handoff protocol.' }
    Write-Output 'Release packaging: hash, sizes, layout, debug exclusion, overwrite, containment and session capability checks passed.'
} finally {
    $resolvedTestRoot = [IO.Path]::GetFullPath($testRoot)
    $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $resolvedTestRoot.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase) -or
        -not ([IO.Path]::GetFileName($resolvedTestRoot).StartsWith('arkya-package-test-'))) {
        throw 'Refusing to remove a directory outside the test workspace.'
    }
    Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force
}
