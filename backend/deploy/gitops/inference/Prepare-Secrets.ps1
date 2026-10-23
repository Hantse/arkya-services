[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$target = Join-Path $PSScriptRoot '.local'
foreach ($name in @('inference.env', 'admin-inference.env')) {
    if (Test-Path -LiteralPath (Join-Path $target $name)) { throw "Refusing to overwrite $name. Rotate both sides deliberately." }
}
$privateMongo = Read-Host 'URI MongoDB avec acces a arkya_inference' -AsSecureString
$mongoUri = [Net.NetworkCredential]::new('', $privateMongo).Password
if ([string]::IsNullOrWhiteSpace($mongoUri) -or $mongoUri.Contains("`r") -or $mongoUri.Contains("`n")) { throw 'Invalid MongoDB URI.' }
$operatorKey = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
New-Item -ItemType Directory -Path $target -Force | Out-Null
if ($IsWindows -or $PSVersionTable.PSEdition -eq 'Desktop') {
    $acl = [Security.AccessControl.DirectorySecurity]::new()
    $acl.SetAccessRuleProtection($true, $false)
    $ownerSid = [Security.Principal.WindowsIdentity]::GetCurrent().User
    $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($ownerSid, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
    Set-Acl -LiteralPath $target -AclObject $acl
} else {
    & chmod 700 $target
    if ($LASTEXITCODE -ne 0) { throw 'Cannot protect secret directory.' }
}
[IO.File]::WriteAllText((Join-Path $target 'inference.env'), "Mongo__ConnectionString=$mongoUri`nInferenceAdmin__Key=$operatorKey`n")
[IO.File]::WriteAllText((Join-Path $target 'admin-inference.env'), "InferenceApi__AdminKey=$operatorKey`n")
Write-Output 'Prepared private files under .local. No cluster changes made.'
