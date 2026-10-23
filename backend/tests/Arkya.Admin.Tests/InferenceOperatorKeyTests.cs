using Arkya.Admin.Inference;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Arkya.Admin.Tests;

public sealed class InferenceOperatorKeyTests
{
    private const string Seed = "administrator-only-seed-kept-in-a-kubernetes-secret";

    [Fact]
    public void ClientAndServerDeriveTheSameDistinctPermissionKey()
    {
        var admin = Configuration();
        var inference = Configuration();
        var key = InferenceOperatorKeys.ResolveAdmin(admin, "");
        Assert.Equal("78FDF2A8E8170DAFBD420BDA523943BAD18161730C9C44157A06D0880C5A0DA0", key);
        Assert.Equal(InferenceOperatorKeys.ResolveInference(inference, ""), key);
        Assert.Equal(64, key.Length);
        Assert.True(key.All(char.IsAsciiHexDigit));
        Assert.NotEqual(Seed, key);
        admin["PlayerAdministration:IdentityKeySeed"] = Seed;
        PlayerAdministrationKeys.ConfigureAdmin(admin);
        Assert.NotEqual(admin["IdentityAdminApi:AdminKey"], key);
    }

    [Fact]
    public void AdminResolvesTheDerivedKeyBeforeBindingInferenceOptions()
    {
        using var factory = new InferenceFactory();
        factory.Settings["InferenceApi:AdminKey"] = "";
        factory.Settings["InferenceOperator:KeySeed"] = Seed;
        var provider = Configuration();
        Assert.Equal(InferenceOperatorKeys.ResolveInference(provider, ""), factory.Services.GetRequiredService<IOptions<InferenceApiOptions>>().Value.AdminKey);
    }

    [Fact]
    public void AnExplicitKeyRemainsSupportedWithoutASeed()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["InferenceApi:AdminKey"] = "explicit-inference-administrator-key-123456" }).Build();
        Assert.Equal("explicit-inference-administrator-key-123456",
            InferenceOperatorKeys.ResolveAdmin(configuration, configuration["InferenceApi:AdminKey"]!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData(" leading-whitespace-is-not-a-supported-seed")]
    [InlineData("a-seed-with-an-embedded-control-character\t-is-invalid")]
    public void MalformedSeedFailsWithoutExposingTheValue(string seed)
    {
        var configuration = Configuration();
        configuration["InferenceOperator:KeySeed"] = seed;
        var error = Assert.Throws<InvalidOperationException>(() => InferenceOperatorKeys.ResolveAdmin(configuration, ""));
        if (seed.Length > 0) Assert.DoesNotContain(seed, error.Message);
        Assert.Null(configuration["InferenceApi:AdminKey"]);
    }

    [Fact]
    public void AmbiguousConfigurationFailsBeforeReplacingAnExistingKey()
    {
        var configuration = Configuration();
        configuration["InferenceAdmin:Key"] = "existing-inference-server-key-1234567890";
        var error = Assert.Throws<InvalidOperationException>(() =>
            InferenceOperatorKeys.ResolveInference(configuration, configuration["InferenceAdmin:Key"]!));
        Assert.DoesNotContain(Seed, error.Message);
        Assert.DoesNotContain(configuration["InferenceAdmin:Key"]!, error.Message);
        Assert.Equal("existing-inference-server-key-1234567890", configuration["InferenceAdmin:Key"]);
    }

    private static IConfigurationRoot Configuration() => new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["InferenceOperator:KeySeed"] = Seed }).Build();
}
