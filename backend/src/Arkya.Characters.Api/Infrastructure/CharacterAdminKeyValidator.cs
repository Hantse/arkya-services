using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Arkya.Characters.Api.Infrastructure;

public sealed class CharacterAdminOptions
{
    public string Key { get; set; } = "";
}

public sealed class CharacterAdminKeyValidator(IOptions<CharacterAdminOptions> options)
{
    public const string HeaderName = "X-Arkya-Admin-Key";

    public bool IsValid(string? value) => options.Value.Key.Length is >= 32 and <= 512 &&
        value is { Length: >= 32 and <= 512 } && CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(options.Value.Key)), SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
