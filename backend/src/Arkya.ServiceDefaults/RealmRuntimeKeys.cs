using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace Arkya.ServiceDefaults;

public static class RealmRuntimeKeys
{
    private const string DedicatedPurpose = "arkya.characters.dedicated.v1";
    private const string DedicatedSeedPath = "RealmRuntime:DedicatedKeySeed";
    private const string DedicatedIdentityPath = "GameAccess:DedicatedServers:0";

    public static string DeriveDedicatedKey(string seed, string realmId, string serverId)
    {
        if (seed is not { Length: >= 32 and <= 512 } || seed != seed.Trim() || seed.Any(char.IsControl))
            throw new InvalidOperationException($"{DedicatedSeedPath} requires 32 to 512 printable characters.");
        if (!GameProtocol.ValidId(realmId) || !GameProtocol.ValidId(serverId))
            throw new InvalidOperationException("Realm runtime requires a valid realm/server binding.");
        return Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(seed),
            Encoding.UTF8.GetBytes($"{DedicatedPurpose}\0{realmId}\0{serverId}")));
    }

    public static void ConfigureCharacters(IConfiguration configuration)
    {
        var seed = configuration[DedicatedSeedPath];
        if (seed is null) return;
        var realmId = configuration["RealmRuntime:RealmId"] ?? "";
        var serverId = configuration["RealmRuntime:ServerId"] ?? "";
        var key = DeriveDedicatedKey(seed, realmId, serverId);
        var identities = configuration.GetSection("GameAccess:DedicatedServers").GetChildren().ToArray();
        if (identities.Any(identity => identity.Key != "0") ||
            !string.IsNullOrEmpty(configuration[$"{DedicatedIdentityPath}:Key"]) ||
            configuration[$"{DedicatedIdentityPath}:RealmId"] is { Length: > 0 } configuredRealm && configuredRealm != realmId ||
            configuration[$"{DedicatedIdentityPath}:ServerId"] is { Length: > 0 } configuredServer && configuredServer != serverId)
            throw new InvalidOperationException("Configure either explicit dedicated identities or RealmRuntime dedicated credentials, never both.");
        configuration[$"{DedicatedIdentityPath}:RealmId"] = realmId;
        configuration[$"{DedicatedIdentityPath}:ServerId"] = serverId;
        configuration[$"{DedicatedIdentityPath}:Key"] = key;
    }
}
