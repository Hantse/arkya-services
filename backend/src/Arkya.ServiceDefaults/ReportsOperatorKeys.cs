using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace Arkya.ServiceDefaults;

public static class ReportsOperatorKeys
{
    private const string SeedPath = "ReportsOperator:KeySeed";
    private const string AdministrationPurpose = "arkya.reports.operator-administration.v1";
    private const string ServicePurpose = "arkya.reports.internal-service.v1";
    private const string McpPurpose = "arkya.reports.mcp-administration.v1";

    public static string ResolveAdmin(IConfiguration configuration, string explicitKey) =>
        Resolve(configuration, explicitKey, "ReportsApi:AdminKey", AdministrationPurpose);

    public static string ResolveReports(IConfiguration configuration, string explicitKey) =>
        Resolve(configuration, explicitKey, "ReportsAdmin:Key", AdministrationPurpose);

    public static string ResolveService(IConfiguration configuration, string explicitKey) =>
        Resolve(configuration, explicitKey, "ServiceAuthentication:Key", ServicePurpose);

    public static string ResolveMcp(IConfiguration configuration, string explicitKey) =>
        Resolve(configuration, explicitKey, "AdminMcp:Key", McpPurpose);

    private static string Resolve(IConfiguration configuration, string explicitKey, string targetPath, string purpose)
    {
        var seed = configuration[SeedPath];
        if (seed is null) return explicitKey;
        if (seed.Length is < 32 or > 512 || seed != seed.Trim() || seed.Any(char.IsControl))
            throw new InvalidOperationException($"{SeedPath} requires 32 to 512 printable characters.");
        if (!string.IsNullOrEmpty(explicitKey))
            throw new InvalidOperationException($"Configure either {targetPath} or {SeedPath}, never both.");
        // Only a purpose-specific derived key crosses each authentication boundary.
        return Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(seed), Encoding.UTF8.GetBytes(purpose)));
    }
}
