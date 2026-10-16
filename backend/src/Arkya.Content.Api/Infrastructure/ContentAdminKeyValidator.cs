using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Arkya.Content.Api.Infrastructure;

public sealed class ContentAdminOptions
{
    public string Key { get; init; } = "";
}

public sealed class ContentAdminKeyValidator(IOptions<ContentAdminOptions> options)
{
    public const string HeaderName = "X-Arkya-Admin-Key";

    public bool IsValid(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 512)
            return false;
        return CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(value)),
            SHA256.HashData(Encoding.UTF8.GetBytes(options.Value.Key)));
    }
}
