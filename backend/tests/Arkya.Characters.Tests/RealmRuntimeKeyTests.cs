using System.Net;
using System.Net.Http.Json;
using Arkya.Characters.Api.Application;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Arkya.Characters.Tests;

public sealed class RealmRuntimeKeyTests
{
    private const string Seed = "realm-runtime-seed-stays-in-kubernetes-secrets";

    [Fact]
    public void CharactersAndDeployerDeriveTheSameDestinationBoundDedicatedCredential()
    {
        var configuration = Configuration();
        RealmRuntimeKeys.ConfigureCharacters(configuration);
        var options = configuration.GetSection("GameAccess").Get<CharacterGameAccessOptions>()!;
        Assert.True(options.IsValid());
        var key = RealmRuntimeKeys.DeriveDedicatedKey(Seed, "beta-web", "island");
        Assert.Equal(64, key.Length);
        Assert.True(key.All(char.IsAsciiHexDigit));
        Assert.DoesNotContain(Seed, key);
        var identity = new DedicatedKeyValidator(Options.Create(options)).Resolve(key);
        Assert.Equal("beta-web", identity!.RealmId);
        Assert.Equal("island", identity.ServerId);
        Assert.Null(new DedicatedKeyValidator(Options.Create(options)).Resolve(Seed));
    }

    [Fact]
    public void DedicatedCredentialsAreStableAndSeparatedBySeedRealmServerAndAdministratorPurpose()
    {
        var key = RealmRuntimeKeys.DeriveDedicatedKey(Seed, "beta-web", "island");
        Assert.Equal(key, RealmRuntimeKeys.DeriveDedicatedKey(Seed, "beta-web", "island"));
        Assert.NotEqual(key, RealmRuntimeKeys.DeriveDedicatedKey(Seed + "-rotated", "beta-web", "island"));
        Assert.NotEqual(key, RealmRuntimeKeys.DeriveDedicatedKey(Seed, "other-realm", "island"));
        Assert.NotEqual(key, RealmRuntimeKeys.DeriveDedicatedKey(Seed, "beta-web", "other-island"));
        var administration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PlayerAdministration:CharactersKeySeed"] = Seed
        }).Build();
        PlayerAdministrationKeys.ConfigureCharacters(administration);
        Assert.NotEqual(administration["AdminAuthentication:Key"], key);
        Assert.NotEqual(administration["ServiceAuthentication:Key"], key);
    }

    [Fact]
    public async Task StartupInstallsDerivedDedicatedCredentialsWithoutAcceptingTheirAdministrativeSeed()
    {
        await using var factory = new CharactersFactory
        {
            AdditionalConfiguration = Configuration().AsEnumerable().ToDictionary(item => item.Key, item => item.Value),
            ConfigureTestServices = services =>
            {
                services.RemoveAll<IGameLeaseStore>();
                services.AddSingleton<IGameLeaseStore, MemoryGameLeaseStore>();
            }
        };
        using var client = factory.PlayerClient(null);
        client.DefaultRequestHeaders.Add("X-Arkya-Dedicated-Key", Seed);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/internal/game/admissions/consume", new { ticket = "invalid" })).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Arkya-Dedicated-Key");
        client.DefaultRequestHeaders.Add("X-Arkya-Dedicated-Key", RealmRuntimeKeys.DeriveDedicatedKey(Seed, "beta-web", "island"));
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PostAsJsonAsync("/internal/game/admissions/consume", new { ticket = "invalid" })).StatusCode);
    }

    [Fact]
    public void ProfilesWithoutSeedPreserveTheirExplicitDedicatedCredentials()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GameAccess:DedicatedServers:0:Key"] = "explicit-dedicated-key-that-must-be-preserved"
        }).Build();
        RealmRuntimeKeys.ConfigureCharacters(configuration);
        Assert.Equal("explicit-dedicated-key-that-must-be-preserved", configuration["GameAccess:DedicatedServers:0:Key"]);
    }

    [Fact]
    public void DeclaredPartitionIdsDeriveOnlyTheApprovedDestinationCredentials()
    {
        var configuration = PartitionConfiguration(4);
        RealmRuntimeKeys.ConfigureCharacters(configuration);
        var options = configuration.GetSection("GameAccess").Get<CharacterGameAccessOptions>()!;
        Assert.True(options.IsValid());
        Assert.Equal(4, options.DedicatedServers.Length);
        var validator = new DedicatedKeyValidator(Options.Create(options));
        for (var index = 0; index < 4; index++)
        {
            var serverId = $"island-starter-island-{index + 1:D2}";
            var identity = validator.Resolve(RealmRuntimeKeys.DeriveDedicatedKey(Seed, "beta-web", serverId));
            Assert.Equal(serverId, identity!.ServerId);
            Assert.Equal("beta-web", identity.RealmId);
        }
        Assert.Null(validator.Resolve(RealmRuntimeKeys.DeriveDedicatedKey(Seed, "beta-web", "undeclared-server")));
        Assert.Null(validator.Resolve(Seed));
    }

    [Fact]
    public void AFullRealmCanDeclareUpTo256DedicatedIdentities()
    {
        var configuration = PartitionConfiguration(256);
        RealmRuntimeKeys.ConfigureCharacters(configuration);
        var options = configuration.GetSection("GameAccess").Get<CharacterGameAccessOptions>()!;
        Assert.Equal(256, options.DedicatedServers.Length);
        Assert.True(options.IsValid());
    }

    [Theory]
    [InlineData("RealmRuntime:ServerId", "legacy-island")]
    [InlineData("RealmRuntime:ServerIds:1", "island-starter-island-01")]
    [InlineData("RealmRuntime:ServerIds:1", "*")]
    [InlineData("RealmRuntime:ServerIds:1", "")]
    [InlineData("RealmRuntime:RealmId", "../unsafe")]
    [InlineData("GameAccess:DedicatedServers:0:Key", "explicit-dedicated-key-that-must-not-change")]
    public void AmbiguousOrInvalidPartitionAllowlistsFailBeforeInstallingAnyKeys(string path, string value)
    {
        var configuration = PartitionConfiguration(4);
        configuration[path] = value;
        var before = configuration.AsEnumerable().OrderBy(item => item.Key).ToArray();
        Assert.Throws<InvalidOperationException>(() => RealmRuntimeKeys.ConfigureCharacters(configuration));
        Assert.Equal(before, configuration.AsEnumerable().OrderBy(item => item.Key));
    }

    [Fact]
    public void OversizedPartitionAllowlistFailsWithoutInstallingCredentials()
    {
        var configuration = PartitionConfiguration(257);
        Assert.Throws<InvalidOperationException>(() => RealmRuntimeKeys.ConfigureCharacters(configuration));
        Assert.Null(configuration["GameAccess:DedicatedServers:0:Key"]);
    }

    [Fact]
    public async Task StartupAcceptsEachDeclaredPartitionCredentialAndRejectsUndeclaredOnes()
    {
        await using var factory = new CharactersFactory
        {
            AdditionalConfiguration = PartitionConfiguration(4).AsEnumerable().ToDictionary(item => item.Key, item => item.Value),
            ConfigureTestServices = services =>
            {
                services.RemoveAll<IGameLeaseStore>();
                services.AddSingleton<IGameLeaseStore, MemoryGameLeaseStore>();
            }
        };
        using var client = factory.PlayerClient(null);
        foreach (var serverId in new[] { "island-starter-island-01", "island-starter-island-02", "island-starter-island-03", "island-starter-island-04", "undeclared-server" })
        {
            client.DefaultRequestHeaders.Remove("X-Arkya-Dedicated-Key");
            client.DefaultRequestHeaders.Add("X-Arkya-Dedicated-Key", RealmRuntimeKeys.DeriveDedicatedKey(Seed, "beta-web", serverId));
            Assert.Equal(serverId == "undeclared-server" ? HttpStatusCode.Forbidden : HttpStatusCode.Conflict,
                (await client.PostAsJsonAsync("/internal/game/admissions/consume", new { ticket = "invalid" })).StatusCode);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData(" leading-whitespace-secret-is-not-supported")]
    [InlineData("control-characters-in-secrets-are-rejected\n")]
    public void InvalidSeedsFailWithoutInstallingOrDisclosingCredentials(string seed)
    {
        var configuration = Configuration();
        configuration["RealmRuntime:DedicatedKeySeed"] = seed;
        var error = Assert.Throws<InvalidOperationException>(() => RealmRuntimeKeys.ConfigureCharacters(configuration));
        if (seed.Length > 0) Assert.DoesNotContain(seed, error.Message);
        Assert.Null(configuration["GameAccess:DedicatedServers:0:Key"]);
        Assert.Null(configuration["GameAccess:DedicatedServers:0:RealmId"]);
    }

    [Theory]
    [InlineData("RealmRuntime:RealmId", "")]
    [InlineData("RealmRuntime:ServerId", "")]
    [InlineData("RealmRuntime:RealmId", "invalid/realm")]
    [InlineData("GameAccess:DedicatedServers:0:Key", "explicit-key-that-must-not-be-overwritten")]
    [InlineData("GameAccess:DedicatedServers:0:RealmId", "other-realm")]
    [InlineData("GameAccess:DedicatedServers:0:ServerId", "other-island")]
    [InlineData("GameAccess:DedicatedServers:1:RealmId", "another-realm")]
    public void InvalidBindingsAndConflictingExplicitIdentitiesFailBeforeAnyMutation(string path, string value)
    {
        var configuration = Configuration();
        configuration[path] = value;
        var before = configuration.AsEnumerable().ToDictionary(item => item.Key, item => item.Value);
        var error = Assert.Throws<InvalidOperationException>(() => RealmRuntimeKeys.ConfigureCharacters(configuration));
        Assert.DoesNotContain(Seed, error.Message);
        Assert.Equal(before.OrderBy(item => item.Key), configuration.AsEnumerable().OrderBy(item => item.Key));
    }

    private static IConfigurationRoot Configuration() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["RealmRuntime:DedicatedKeySeed"] = Seed,
        ["RealmRuntime:RealmId"] = "beta-web",
        ["RealmRuntime:ServerId"] = "island"
    }).Build();

    private static IConfigurationRoot PartitionConfiguration(int count)
    {
        var values = new Dictionary<string, string?>
        {
            ["RealmRuntime:DedicatedKeySeed"] = Seed,
            ["RealmRuntime:RealmId"] = "beta-web"
        };
        for (var index = 0; index < count; index++) values[$"RealmRuntime:ServerIds:{index}"] = $"island-starter-island-{index + 1:D2}";
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
