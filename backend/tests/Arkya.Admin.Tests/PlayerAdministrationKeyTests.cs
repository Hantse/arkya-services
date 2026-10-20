using Arkya.ServiceDefaults;
using Microsoft.Extensions.Configuration;

namespace Arkya.Admin.Tests;

public sealed class PlayerAdministrationKeyTests
{
    private const string Seed = "administrator-only-seed-kept-in-a-kubernetes-secret";

    [Fact]
    public void AdministratorClientsAndProvidersDeriveMatchingKeysWithSeparatedServicePermissions()
    {
        var admin = Configuration();
        var identity = Configuration();
        var characters = Configuration();
        PlayerAdministrationKeys.ConfigureAdmin(admin);
        PlayerAdministrationKeys.ConfigureIdentity(identity);
        PlayerAdministrationKeys.ConfigureCharacters(characters);
        var accountKey = admin["IdentityAdminApi:AdminKey"]!;
        var characterKey = admin["CharactersAdminApi:AdminKey"]!;
        var serviceKey = characters["ServiceAuthentication:Key"]!;
        Assert.Equal("7C623EE441FEC6778BA42630B0999722135C10393162D1BC98530CD2CB898461", accountKey);
        Assert.Equal(identity["Administration:AdminKey"], accountKey);
        Assert.Equal(characters["AdminAuthentication:Key"], characterKey);
        Assert.Equal(3, new[] { accountKey, characterKey, serviceKey }.Distinct().Count());
        Assert.All(new[] { accountKey, characterKey, serviceKey }, key =>
        {
            Assert.Equal(64, key.Length);
            Assert.DoesNotContain(Seed, key);
            Assert.True(key.All(char.IsAsciiHexDigit));
        });
    }

    [Fact]
    public void ExplicitExistingKeysArePreservedWhenTheProfileHasNoSeeds()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Administration:AdminKey"] = "an-explicit-key-that-must-remain-unchanged"
        }).Build();
        PlayerAdministrationKeys.ConfigureIdentity(configuration);
        Assert.Equal("an-explicit-key-that-must-remain-unchanged", configuration["Administration:AdminKey"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData(" leading-whitespace-is-not-a-supported-seed")]
    [InlineData("control-characters-in-this-secret-are-rejected\n")]
    public void InvalidSeedsFailWithoutDisclosingTheirValues(string seed)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PlayerAdministration:IdentityKeySeed"] = seed
        }).Build();
        var error = Assert.Throws<InvalidOperationException>(() => PlayerAdministrationKeys.ConfigureIdentity(configuration));
        if (seed.Length > 0) Assert.DoesNotContain(seed, error.Message);
        Assert.Null(configuration["Administration:AdminKey"]);
    }

    [Fact]
    public void AmbiguousRawAndDerivedKeysFailInsteadOfSilentlyRotatingCredentials()
    {
        var configuration = Configuration();
        configuration["Administration:AdminKey"] = "existing-explicit-admin-key-kept-private";
        var error = Assert.Throws<InvalidOperationException>(() => PlayerAdministrationKeys.ConfigureIdentity(configuration));
        Assert.DoesNotContain(Seed, error.Message);
        Assert.DoesNotContain(configuration["Administration:AdminKey"]!, error.Message);
    }

    [Fact]
    public void AllMappingsAreValidatedBeforeAnyKeyIsInstalled()
    {
        var configuration = Configuration();
        configuration["ServiceAuthentication:Key"] = "existing-service-key-that-must-be-preserved";
        Assert.Throws<InvalidOperationException>(() => PlayerAdministrationKeys.ConfigureCharacters(configuration));
        Assert.Null(configuration["AdminAuthentication:Key"]);
    }

    private static IConfigurationRoot Configuration() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["PlayerAdministration:IdentityKeySeed"] = Seed,
        ["PlayerAdministration:CharactersKeySeed"] = Seed
    }).Build();
}
