using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace Arkya.ServiceDefaults;

public static class NpcDialogueKeys
{
    private const string SeedPath = "NpcDialogue:KeySeed";
    private const string Purpose = "arkya.inference.npc-dialogue.v1";

    public static string ResolveInference(IConfiguration configuration, string explicitKey) =>
        Resolve(configuration, explicitKey, "NpcDialogue:ServiceKey");

    public static string ResolveRealms(IConfiguration configuration, string explicitKey) =>
        Resolve(configuration, explicitKey, "RealmRuntime:InferenceServiceKey");

    private static string Resolve(IConfiguration configuration, string explicitKey, string targetPath)
    {
        var seed = configuration[SeedPath];
        if (seed is null) return explicitKey;
        if (seed.Length is < 32 or > 512 || seed != seed.Trim() || seed.Any(char.IsControl))
            throw new InvalidOperationException($"{SeedPath} requires 32 to 512 printable characters.");
        if (!string.IsNullOrEmpty(explicitKey))
            throw new InvalidOperationException($"Configure either {targetPath} or {SeedPath}, never both.");
        // Only the dialogue-specific key is delivered to dedicated servers.
        return Convert.ToHexString(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(seed), Encoding.UTF8.GetBytes(Purpose)));
    }
}
