using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Arkya.Reports.Api.Infrastructure;

public sealed class ReportsAdminOptions
{
    public string Key { get; set; } = "";
    public string Actor { get; set; } = "reports-admin";
}

public sealed class ReportsAdminKeyValidator(IOptions<ReportsAdminOptions> options)
{
    public const string HeaderName = "X-Arkya-Reports-Admin-Key";
    public const string ActorHeaderName = "X-Arkya-Admin-Actor";

    public bool IsValid(string? value) => !string.IsNullOrEmpty(value) && value.Length <= 512 &&
        CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(value)),
            SHA256.HashData(Encoding.UTF8.GetBytes(options.Value.Key)));
}
