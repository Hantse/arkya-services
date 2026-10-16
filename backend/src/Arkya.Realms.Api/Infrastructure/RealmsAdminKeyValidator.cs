using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Arkya.Realms.Api.Infrastructure;

public sealed class RealmsAdminOptions
{
    public string Key { get; set; } = "";
}

public sealed class RealmsAdminKeyValidator(IOptions<RealmsAdminOptions> options)
{
    public const string HeaderName = "X-Arkya-Admin-Key";
    private readonly byte[] expected = SHA256.HashData(Encoding.UTF8.GetBytes(options.Value.Key));
    public bool IsValid(string? value) => value is { Length: >= 32 and <= 512 } &&
        CryptographicOperations.FixedTimeEquals(expected, SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
