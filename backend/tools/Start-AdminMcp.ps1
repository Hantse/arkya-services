[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$project = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../src/Arkya.Admin.Mcp/Arkya.Admin.Mcp.csproj'))
$assembly = Join-Path (Split-Path $project -Parent) 'bin/Release/net10.0/Arkya.Admin.Mcp.dll'

# stdout is reserved for the MCP protocol, including on a clean checkout.
& dotnet build $project --configuration Release --nologo --verbosity quiet 2>&1 |
    ForEach-Object { [Console]::Error.WriteLine($_.ToString()) }
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& dotnet $assembly
exit $LASTEXITCODE
