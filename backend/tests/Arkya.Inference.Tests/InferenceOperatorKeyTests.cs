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
    public void ServiceRejectsAmbiguousOrOversizedSeedConfiguration()
    {
        using var ambiguous = new InferenceFactory();
        ambiguous.Settings["InferenceOperator:KeySeed"] = Seed;
        Assert.Throws<InvalidOperationException>(() => ambiguous.Client());
        using var oversized = new InferenceFactory();
        oversized.Settings["InferenceAdmin:Key"] = "";
        oversized.Settings["InferenceOperator:KeySeed"] = new string('a', 513);
        Assert.Throws<InvalidOperationException>(() => oversized.Client());
    }
}
