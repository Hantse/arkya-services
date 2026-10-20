using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace Arkya.ServiceDefaults;

public static class PlayerAdministrationKeys
{
    private const string IdentityPurpose = "arkya.identity.account-administration.v1";
    private const string CharactersPurpose = "arkya.characters.administration.v1";
    private const string CharactersServicePurpose = "arkya.characters.internal-service.v1";
    private const string IdentitySeed = "PlayerAdministration:IdentityKeySeed";
    private const string CharactersSeed = "PlayerAdministration:CharactersKeySeed";

    public static void ConfigureAdmin(IConfiguration configuration)
    {
        Configure(configuration, (IdentitySeed, IdentityPurpose, "IdentityAdminApi:AdminKey"),
            (CharactersSeed, CharactersPurpose, "CharactersAdminApi:AdminKey"));
    }

    public static void ConfigureIdentity(IConfiguration configuration) =>
        Configure(configuration, (IdentitySeed, IdentityPurpose, "Administration:AdminKey"));

    public static void ConfigureCharacters(IConfiguration configuration)
    {
        Configure(configuration, (CharactersSeed, CharactersPurpose, "AdminAuthentication:Key"),
            (CharactersSeed, CharactersServicePurpose, "ServiceAuthentication:Key"));
    }

    private static void Configure(IConfiguration configuration, params (string SeedPath, string Purpose, string TargetPath)[] mappings)
    {
        var derived = new Dictionary<string, string>();
        foreach (var (seedPath, purpose, targetPath) in mappings)
        {
            var seed = configuration[seedPath];
            if (seed is null) continue;
            if (seed.Length is < 32 or > 512 || seed != seed.Trim() || seed.Any(char.IsControl))
                throw new InvalidOperationException($"{seedPath} requires 32 to 512 printable characters.");
            if (!string.IsNullOrEmpty(configuration[targetPath]))
                throw new InvalidOperationException($"Configure either {targetPath} or {seedPath}, never both.");
            // Wire keys remain separate for each permission boundary, even on the private GitOps profile.
            derived[targetPath] = Convert.ToHexString(HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(seed), Encoding.UTF8.GetBytes(purpose)));
        }
        foreach (var (path, value) in derived) configuration[path] = value;
    }
}
