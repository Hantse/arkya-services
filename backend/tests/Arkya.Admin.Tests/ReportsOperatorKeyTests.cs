using Arkya.Admin.Reports;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Arkya.Admin.Tests;

public sealed class ReportsOperatorKeyTests
{
    internal const string Seed = "administrator-only-reports-seed-in-an-existing-secret";

    [Fact]
    public void BothServicesAgreeOnAdministrationWhileMcpAndServicePermissionsAreSeparated()
    {
        var configuration = Configuration();
        var admin = ReportsOperatorKeys.ResolveAdmin(configuration, "");
        Assert.Equal(ReportsOperatorKeys.ResolveReports(configuration, ""), admin);
        var keys = new[] { admin, ReportsOperatorKeys.ResolveMcp(configuration, ""), ReportsOperatorKeys.ResolveService(configuration, "") };
        Assert.Equal(3, keys.Distinct().Count());
        Assert.All(keys, key =>
        {
            Assert.Equal(64, key.Length);
            Assert.True(key.All(char.IsAsciiHexDigit));
            Assert.NotEqual(Seed, key);
        });
        configuration["InferenceOperator:KeySeed"] = Seed;
        Assert.DoesNotContain(InferenceOperatorKeys.ResolveAdmin(configuration, ""), keys);
    }

    [Fact]
    public void AdminResolvesBothKeysBeforeOptionsValidation()
    {
        using var factory = new AdminFactory();
        factory.Settings["ReportsApi:Enabled"] = "true";
        factory.Settings["AdminMcp:Enabled"] = "true";
        factory.Settings["ReportsApi:AdminKey"] = "";
        factory.Settings["AdminMcp:Key"] = "";
        factory.Settings["ReportsOperator:KeySeed"] = Seed;
        Assert.Equal(ReportsOperatorKeys.ResolveReports(Configuration(), ""),
            factory.Services.GetRequiredService<IOptions<ReportsApiOptions>>().Value.AdminKey);
        Assert.Equal(ReportsOperatorKeys.ResolveMcp(Configuration(), ""),
            factory.Services.GetRequiredService<IOptions<AdminMcpOptions>>().Value.Key);
    }

    [Fact]
    public void ExplicitKeysRemainUnchangedWhenThereIsNoSeed()
    {
        var configuration = new ConfigurationBuilder().Build();
        const string explicitKey = "an-explicit-reports-key-kept-for-direct-deployment";
        Assert.Equal(explicitKey, ReportsOperatorKeys.ResolveAdmin(configuration, explicitKey));
        Assert.Equal(explicitKey, ReportsOperatorKeys.ResolveReports(configuration, explicitKey));
        Assert.Equal(explicitKey, ReportsOperatorKeys.ResolveService(configuration, explicitKey));
        Assert.Equal(explicitKey, ReportsOperatorKeys.ResolveMcp(configuration, explicitKey));
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData(" leading-whitespace-is-not-a-supported-seed")]
    [InlineData("a-seed-with-an-embedded-control-character\t-is-invalid")]
    public void InvalidSeedsAreRejectedWithoutDisclosure(string seed)
    {
        var configuration = Configuration();
        configuration["ReportsOperator:KeySeed"] = seed;
        var error = Assert.Throws<InvalidOperationException>(() => ReportsOperatorKeys.ResolveAdmin(configuration, ""));
        if (seed.Length > 0) Assert.DoesNotContain(seed, error.Message);
    }

    [Fact]
    public void OversizedSeedAndAmbiguousExplicitKeysAreRejectedWithoutDisclosure()
    {
        var configuration = Configuration();
        const string explicitKey = "an-explicit-key-that-must-never-be-overwritten";
        foreach (var resolve in new Func<IConfiguration, string, string>[]
        {
            ReportsOperatorKeys.ResolveAdmin, ReportsOperatorKeys.ResolveReports,
            ReportsOperatorKeys.ResolveService, ReportsOperatorKeys.ResolveMcp
        })
        {
            var error = Assert.Throws<InvalidOperationException>(() => resolve(configuration, explicitKey));
            Assert.DoesNotContain(Seed, error.Message);
            Assert.DoesNotContain(explicitKey, error.Message);
        }
        configuration["ReportsOperator:KeySeed"] = new string('a', 513);
        Assert.Throws<InvalidOperationException>(() => ReportsOperatorKeys.ResolveMcp(configuration, ""));
    }

    internal static IConfigurationRoot Configuration() => new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["ReportsOperator:KeySeed"] = Seed }).Build();
}
