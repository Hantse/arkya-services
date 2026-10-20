using System.Net;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Configuration;

namespace Arkya.Inference.Tests;

public sealed class InferenceOperatorKeyTests
{
    private const string Seed = "administrator-only-seed-kept-in-a-kubernetes-secret";

    [Fact]
    public async Task ServiceAcceptsTheDerivedClientKeyAndRejectsTheUnderlyingSeed()
    {
        using var factory = new InferenceFactory();
        factory.Settings["InferenceAdmin:Key"] = "";
        factory.Settings["InferenceOperator:KeySeed"] = Seed;
        var clientConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["InferenceOperator:KeySeed"] = Seed }).Build();
        using var client = factory.Client(InferenceOperatorKeys.ResolveAdmin(clientConfig, ""));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/admin/inference/agents")).StatusCode);
        using var rawSeed = factory.Client(Seed);
        Assert.Equal(HttpStatusCode.Forbidden, (await rawSeed.GetAsync("/admin/inference/agents")).StatusCode);
    }

    [Fact]
    public void SeedResolverRejectsAmbiguousOrOversizedConfigurationWithoutExposingValues()
    {
        var ambiguous = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["InferenceOperator:KeySeed"] = Seed,
            ["InferenceAdmin:Key"] = InferenceFactory.AdminKey
        }).Build();
        var ambiguity = Assert.Throws<InvalidOperationException>(() =>
            InferenceOperatorKeys.ResolveInference(ambiguous, ambiguous["InferenceAdmin:Key"]!));
        Assert.Equal("Configure either InferenceAdmin:Key or InferenceOperator:KeySeed, never both.", ambiguity.Message);
        Assert.DoesNotContain(Seed, ambiguity.Message);
        Assert.DoesNotContain(InferenceFactory.AdminKey, ambiguity.Message);

        var oversizedSeed = new string('a', 513);
        var oversized = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["InferenceOperator:KeySeed"] = oversizedSeed }).Build();
        var invalidSize = Assert.Throws<InvalidOperationException>(() =>
            InferenceOperatorKeys.ResolveInference(oversized, ""));
        Assert.Equal("InferenceOperator:KeySeed requires 32 to 512 printable characters.", invalidSize.Message);
        Assert.DoesNotContain(oversizedSeed, invalidSize.Message);
    }
}
