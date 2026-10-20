using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Arkya.ServiceDefaults;

public sealed class ServiceAuthenticationOptions
{
    public string Key { get; set; } = "";
}

public sealed class ServiceKeyValidator(IOptions<ServiceAuthenticationOptions> options)
{
    public const string HeaderName = "X-Arkya-Service-Key";

    public bool IsValid(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 512)
        {
            return false;
        }

        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(options.Value.Key));
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
