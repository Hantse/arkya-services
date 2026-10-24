using Arkya.Realms.Api.Application;
using Arkya.Realms.Api.Infrastructure;

namespace Arkya.Realms.Tests;

public sealed class RealmServiceRoutingTests
{
    private static RealmRuntimeOptions Runtime() => new()
    {
        DedicatedKeySeed = new string('s', 32),
        CharactersBaseUrl = "https://characters.example/",
        Services =
        [
            new() { RealmId = "eu-one", CharactersBaseUrl = "https://characters-eu.example/" },
            new() { RealmId = "us-one", CharactersBaseUrl = "https://characters-us.example/" }
        ]
    };

    [Theory]
    [InlineData("eu-one", "https://characters-eu.example/")]
    [InlineData("us-one", "https://characters-us.example/")]
    [InlineData("common-world", "https://characters.example/")]
    public void DedicatedUsesTheConfiguredRealmServiceOrTheCommonFacade(string realmId, string expectedUrl)
    {
        var runtime = Runtime();
        Assert.True(runtime.IsValid());
        Assert.Equal(expectedUrl, runtime.CharactersUrlFor(realmId));
        var realm = Fixtures.Realm() with { RealmId = realmId };
        var manifest = KubernetesRealmDeployer.DeploymentManifest(realm, realm.Profile.Servers[0], runtime);
        var environment = manifest["spec"]!["template"]!["spec"]!["containers"]![0]!["env"]!.AsArray();
        var entry = Assert.Single(environment, value => value!["name"]!.GetValue<string>() == "ARKYA_CHARACTERS_URL");
        Assert.Equal(expectedUrl, entry!["value"]!.GetValue<string>());
        Assert.DoesNotContain(environment, value => value!["name"]!.GetValue<string>() == "ARKYA_REALMS_API_URL");
        Assert.DoesNotContain(runtime.DedicatedKeySeed, manifest.ToJsonString());
    }

    [Theory]
    [InlineData("https://realms.example/", false, true)]
    [InlineData("http://arkya-realms.arkya-beta.svc.cluster.local:8080/", true, true)]
    [InlineData("http://arkya-realms.arkya-beta.svc.cluster.local:8080/", false, false)]
    [InlineData("http://other.arkya-beta.svc.cluster.local:8080/", true, false)]
    [InlineData("http://arkya-realms.arkya-beta.svc.cluster.local:8081/", true, false)]
    [InlineData("https://user:secret@realms.example/", false, false)]
    [InlineData("https://realms.example/?key=secret", false, false)]
    [InlineData("https://realms.example/#fragment", false, false)]
    [InlineData("https://realms.example/path", false, false)]
    public void ExplicitCommandOriginIsValidatedAndInjectedIndependentlyOfCharactersRouting(
        string address, bool allowClusterHttp, bool valid)
    {
        var runtime = Runtime();
        runtime.RealmsBaseUrl = address;
        runtime.AllowClusterHttp = allowClusterHttp;
        Assert.Equal(valid, runtime.IsValid());
        if (!valid) return;
        var realm = Fixtures.Realm();
        var environment = KubernetesRealmDeployer.DeploymentManifest(realm, realm.Profile.Servers[0], runtime)
            ["spec"]!["template"]!["spec"]!["containers"]![0]!["env"]!.AsArray();
        var entry = Assert.Single(environment, value => value!["name"]!.GetValue<string>() == "ARKYA_REALMS_API_URL");
        Assert.Equal(address, entry!["value"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("EU-ONE")]
    [InlineData("eu-*")]
    [InlineData("../eu-one")]
    [InlineData("eu-one ")]
    public void RealmServiceRequiresAnExactRealmIdentifier(string realmId)
    {
        var runtime = Runtime();
        runtime.Services[0].RealmId = realmId;
        Assert.False(runtime.IsValid());
    }

    [Fact]
    public void DuplicateRealmServicesAreRejectedBeforeDeployment()
    {
        var runtime = Runtime();
        runtime.Services[1].RealmId = runtime.Services[0].RealmId;
        Assert.False(runtime.IsValid());
        Assert.Throws<DeploymentFailure>(() => KubernetesRealmDeployer.DeploymentManifest(
            Fixtures.Realm(), Fixtures.Realm().Profile.Servers[0], runtime));
    }

    [Theory]
    [InlineData("https://characters-eu.example/", false, true)]
    [InlineData("http://arkya-characters.arkya-beta.svc.cluster.local:8080/", true, true)]
    [InlineData("http://arkya-characters.arkya-beta.svc.cluster.local:8080/", false, false)]
    [InlineData("http://other.arkya-beta.svc.cluster.local:8080/", true, false)]
    [InlineData("http://characters-eu.example/", true, false)]
    [InlineData("http://localhost:8080/", true, false)]
    [InlineData("https://user:secret@characters-eu.example/", false, false)]
    [InlineData("https://characters-eu.example/?secret=value", false, false)]
    [InlineData("https://characters-eu.example/#fragment", false, false)]
    [InlineData("https://characters-eu.example/path", false, false)]
    [InlineData("https://characters-eu.example\\path", false, false)]
    [InlineData("https://characters-eu.example/ ", false, false)]
    [InlineData("", false, false)]
    public void RealmServiceUsesTheExistingDedicatedTransportRules(string address, bool allowClusterHttp, bool valid)
    {
        var runtime = Runtime();
        runtime.AllowClusterHttp = allowClusterHttp;
        runtime.Services[0].CharactersBaseUrl = address;
        Assert.Equal(valid, runtime.IsValid());
    }

    [Fact]
    public void RealmServicesRequireAnEnabledRuntimeAndACommonFallback()
    {
        var runtime = Runtime();
        runtime.DedicatedKeySeed = "";
        runtime.CharactersBaseUrl = "";
        Assert.False(runtime.IsValid());
        runtime.DedicatedKeySeed = new string('s', 32);
        Assert.False(runtime.IsValid());
    }

    [Fact]
    public void ExplicitCommandOriginCannotEnableAnUnconfiguredRuntime()
    {
        var runtime = new RealmRuntimeOptions { RealmsBaseUrl = "https://realms.example/" };
        Assert.False(runtime.IsValid());
    }

    [Fact]
    public void RealmServicesAreBoundedAndRejectNullConfigurationEntries()
    {
        var runtime = Runtime();
        runtime.Services = null!;
        Assert.False(runtime.IsValid());
        runtime.Services = [null!];
        Assert.False(runtime.IsValid());
        runtime.Services = Enumerable.Range(0, 101).Select(index => new RealmServiceOptions
            { RealmId = $"realm-{index}", CharactersBaseUrl = "https://characters.example/" }).ToArray();
        Assert.False(runtime.IsValid());
    }
}
