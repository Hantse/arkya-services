using Arkya.Realms.Api.Application;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Arkya.Realms.Tests;

public sealed class NpcDialogueKeyTests
{
    private const string Seed = "administrator-only-seed-kept-in-a-kubernetes-secret";

    [Fact]
    public void RealmRuntimeResolvesTheSameDialogueCredentialAsInferenceBeforeValidation()
    {
        using var factory = Enabled();
        factory.Settings["NpcDialogue:KeySeed"] = Seed;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["NpcDialogue:KeySeed"] = Seed }).Build();
        var runtime = factory.Services.GetRequiredService<IOptions<RealmRuntimeOptions>>().Value;
        Assert.Equal(NpcDialogueKeys.ResolveInference(configuration, ""), runtime.InferenceServiceKey);
        Assert.True(runtime.IsValid());
    }

    [Fact]
    public void ExplicitRuntimeCredentialRemainsSupportedWithoutASeed()
    {
        using var factory = Enabled();
        const string key = "explicit-npc-dialogue-service-key-123456789";
        factory.Settings["RealmRuntime:InferenceServiceKey"] = key;
        Assert.Equal(key, factory.Services.GetRequiredService<IOptions<RealmRuntimeOptions>>().Value.InferenceServiceKey);
    }

    [Fact]
    public void UnconfiguredDialogueRuntimeRemainsDisabled()
    {
        using var factory = new RealmFactory();
        var runtime = factory.Services.GetRequiredService<IOptions<RealmRuntimeOptions>>().Value;
        Assert.Empty(runtime.InferenceBaseUrl);
        Assert.Empty(runtime.InferenceServiceKey);
    }

    private static RealmFactory Enabled()
    {
        var factory = new RealmFactory();
        factory.Settings["RealmRuntime:DedicatedKeySeed"] = new string('d', 32);
        factory.Settings["RealmRuntime:CharactersBaseUrl"] = "https://characters.example/";
        factory.Settings["RealmRuntime:InferenceBaseUrl"] = "https://inference.example/";
        return factory;
    }
}
