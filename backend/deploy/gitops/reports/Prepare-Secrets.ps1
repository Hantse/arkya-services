[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$target = Join-Path $PSScriptRoot '.local'
$files = @('reports.env', 'admin-reports.env', 'mcp-token')
foreach ($file in $files) {
    if (Test-Path -LiteralPath (Join-Path $target $file)) { throw "Refusing to overwrite $file. Rotate both sides deliberately." }
}
function Read-PrivateValue([string] $prompt) {
    $value = Read-Host $prompt -AsSecureString
    $plain = [System.Net.NetworkCredential]::new('', $value).Password
    if ([string]::IsNullOrWhiteSpace($plain) -or $plain.Contains("`r") -or $plain.Contains("`n")) { throw 'Invalid secret.' }
    return $plain
}
$mongo = Read-PrivateValue 'URI MongoDB avec acces a arkya_reports'
$identityKey = Read-PrivateValue 'ServiceAuthentication__Key existante dans Identity'
if ($identityKey.Length -lt 32 -or $identityKey.Length -gt 512) { throw 'Identity key must contain 32-512 characters.' }
$reportsKey = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$mcpKey = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$serviceKey = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
New-Item -ItemType Directory -Path $target -Force | Out-Null
if ($IsWindows) {
    $acl = [Security.AccessControl.DirectorySecurity]::new()
    $acl.SetAccessRuleProtection($true, $false)
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User
    $rule = [Security.AccessControl.FileSystemAccessRule]::new($sid, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
    $acl.AddAccessRule($rule)
    Set-Acl -LiteralPath $target -AclObject $acl
} else {
    & chmod 700 $target
    if ($LASTEXITCODE -ne 0) { throw 'Cannot protect secret directory.' }
}
[IO.File]::WriteAllText((Join-Path $target 'reports.env'), "Mongo__ConnectionString=$mongo`nIdentity__ServiceKey=$identityKey`nReportsAdmin__Key=$reportsKey`nServiceAuthentication__Key=$serviceKey`n")
[IO.File]::WriteAllText((Join-Path $target 'admin-reports.env'), "ReportsApi__AdminKey=$reportsKey`nAdminMcp__Key=$mcpKey`n")
[IO.File]::WriteAllText((Join-Path $target 'mcp-token'), $mcpKey)
Write-Output 'Prepared private files under .local. No cluster changes made; no existing secrets replaced.'
