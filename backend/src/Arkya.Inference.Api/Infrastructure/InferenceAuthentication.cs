using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Arkya.Inference.Api.Infrastructure;

public sealed class InferenceAdminOptions
{
    public string Key { get; set; } = "";
}

public sealed class InferenceAdminKeyValidator(IOptions<InferenceAdminOptions> options)
{
    public const string HeaderName = "X-Arkya-Admin-Key";
    private readonly byte[] expected = SHA256.HashData(Encoding.UTF8.GetBytes(options.Value.Key));
    public bool IsValid(string? value) => options.Value.Key is { Length: >= 32 and <= 512 } &&
        value is { Length: >= 32 and <= 512 } &&
        CryptographicOperations.FixedTimeEquals(expected, SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public static class AgentCredentials
{
    public static string Create() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
    public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    public static bool IsValidToken(string? token) => token is { Length: 64 } && token.All(char.IsAsciiHexDigit);

    public static bool Matches(string? token, string expectedHash) =>
        IsValidToken(token) &&
        expectedHash is { Length: 64 } && expectedHash.All(char.IsAsciiHexDigit) &&
        CryptographicOperations.FixedTimeEquals(Convert.FromHexString(Hash(token!)), Convert.FromHexString(expectedHash));
}
