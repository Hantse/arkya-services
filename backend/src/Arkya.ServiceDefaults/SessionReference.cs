using System.Security.Cryptography;
using System.Text;

namespace Arkya.ServiceDefaults;

public static class SessionReference
{
    public const string ClaimType = "arkya:session-reference";
    public static string Create(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    public static bool IsValid(string? value) => value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F');
}
