using System.Net;
using Arkya.Inference.Api.Application;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Arkya.Inference.Tests;

public sealed class NpcDialogueKeyTests
{
    private const string Seed = "administrator-only-seed-kept-in-a-kubernetes-secret";

    [Fact]
    public void ClientAndServerDeriveTheSameKeyWithASeparateDialoguePurpose()
    {
        var configuration = Configuration(Seed);
        var key = NpcDialogueKeys.ResolveInference(configuration, "");
        Assert.Equal("95C0930DEE850A199D1390C12DBFB0614B47238A7A4BCB3B08A02CB626D45C63", key);
        Assert.Equal(key, NpcDialogueKeys.ResolveRealms(configuration, ""));
        Assert.NotEqual(Seed, key);
        configuration["InferenceOperator:KeySeed"] = Seed;
        Assert.NotEqual(InferenceOperatorKeys.ResolveInference(configuration, ""), key);
    }

    [Fact]
    public async Task InferenceAuthenticatesTheDerivedDialogueKeyAndRejectsSeedAndOperatorKey()
    {
        using var factory = new InferenceFactory();
        factory.Settings["NpcDialogue:Enabled"] = "true";
        factory.Settings["NpcDialogue:KeySeed"] = Seed;
        var configuration = Configuration(Seed);
        var key = NpcDialogueKeys.ResolveRealms(configuration, "");
        using var dedicated = Client(key);
        Assert.Equal(key, factory.Services.GetRequiredService<IOptions<NpcDialogueOptions>>().Value.ServiceKey);
        var route = "/internal/inference/dialogues/" + Guid.NewGuid().ToString("N");
        Assert.Equal(HttpStatusCode.NotFound, (await dedicated.GetAsync(route)).StatusCode);
        using var seedClient = Client(Seed);
        Assert.Equal(HttpStatusCode.Forbidden, (await seedClient.GetAsync(route)).StatusCode);
        configuration["InferenceOperator:KeySeed"] = Seed;
        using var operatorClient = Client(InferenceOperatorKeys.ResolveAdmin(configuration, ""));
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.GetAsync(route)).StatusCode);

        HttpClient Client(string serviceKey)
        {
            var client = factory.Client(null);
            client.DefaultRequestHeaders.Add("X-Arkya-Service-Key", serviceKey);
            return client;
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("explicit-npc-dialogue-service-key-123456789")]
    public void WithoutASeedExistingKeysAndTheDisabledDefaultRemainUnchanged(string key)
    {
        var configuration = new ConfigurationBuilder().Build();
        Assert.Equal(key, NpcDialogueKeys.ResolveInference(configuration, key));
        Assert.Equal(key, NpcDialogueKeys.ResolveRealms(configuration, key));
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData(" leading-whitespace-is-not-a-supported-seed")]
    [InlineData("trailing-whitespace-is-not-a-supported-seed ")]
    [InlineData("a-seed-with-an-embedded-control-character\t-is-invalid")]
    public void MalformedSeedFailsWithoutExposingTheValue(string seed)
    {
        foreach (var resolve in Resolvers())
        {
            var error = Assert.Throws<InvalidOperationException>(() => resolve(Configuration(seed), ""));
            Assert.Equal("NpcDialogue:KeySeed requires 32 to 512 printable characters.", error.Message);
            if (seed.Length > 0) Assert.DoesNotContain(seed, error.Message);
        }
    }

    [Fact]
    public void OversizedSeedIsRejectedWithoutExposingTheValue()
    {
        var seed = new string('a', 513);
        foreach (var resolve in Resolvers())
        {
            var error = Assert.Throws<InvalidOperationException>(() => resolve(Configuration(seed), ""));
            Assert.Equal("NpcDialogue:KeySeed requires 32 to 512 printable characters.", error.Message);
            Assert.DoesNotContain(seed, error.Message);
        }
    }

    [Fact]
    public void SeedAndExplicitCredentialAreRejectedForBothConfigurationPaths()
    {
        const string key = "explicit-npc-dialogue-service-key-123456789";
        var configuration = Configuration(Seed);
        var inference = Assert.Throws<InvalidOperationException>(() => NpcDialogueKeys.ResolveInference(configuration, key));
        var realms = Assert.Throws<InvalidOperationException>(() => NpcDialogueKeys.ResolveRealms(configuration, key));
        Assert.Equal("Configure either NpcDialogue:ServiceKey or NpcDialogue:KeySeed, never both.", inference.Message);
        Assert.Equal("Configure either RealmRuntime:InferenceServiceKey or NpcDialogue:KeySeed, never both.", realms.Message);
        foreach (var error in new[] { inference, realms })
        {
            Assert.DoesNotContain(Seed, error.Message);
            Assert.DoesNotContain(key, error.Message);
        }
    }

    private static Func<IConfiguration, string, string>[] Resolvers() =>
        [NpcDialogueKeys.ResolveInference, NpcDialogueKeys.ResolveRealms];

    private static IConfigurationRoot Configuration(string seed) => new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["NpcDialogue:KeySeed"] = seed }).Build();
}
