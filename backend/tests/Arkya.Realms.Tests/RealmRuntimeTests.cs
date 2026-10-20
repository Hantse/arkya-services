using System.Text.Json.Nodes;
using Arkya.Realms.Api.Application;
using Arkya.Realms.Api.Contracts;
using Arkya.Realms.Api.Infrastructure;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Options;

namespace Arkya.Realms.Tests;

public sealed class RealmRuntimeTests
{
    [Theory]
    [InlineData("https://characters.example/", false, true)]
    [InlineData("http://arkya-characters.arkya-beta.svc.cluster.local:8080/", true, true)]
    [InlineData("http://arkya-characters.arkya-beta.svc.cluster.local:8080/", false, false)]
    [InlineData("http://characters.example/", true, false)]
    [InlineData("http://other.arkya-beta.svc.cluster.local:8080/", true, false)]
    [InlineData("http://arkya-characters.arkya-beta.svc.cluster.local:8081/", true, false)]
    [InlineData("http://arkya-characters.arkya-beta.svc.cluster.local.evil:8080/", true, false)]
    [InlineData("https://user:secret@characters.example/", false, false)]
    [InlineData("https://characters.example/?key=secret", false, false)]
    public void RuntimeTransportRequiresAnExplicitPrivateOrigin(string address, bool allowHttp, bool valid)
    {
        var options = new RealmRuntimeOptions { DedicatedKeySeed = new string('s', 32), CharactersBaseUrl = address, AllowClusterHttp = allowHttp };
        Assert.Equal(valid, options.IsValid());
    }

    [Fact]
    public void RuntimeRejectsControlCharactersAndRelativeRegistryFiles()
    {
        var options = new RealmRuntimeOptions { DedicatedKeySeed = new string('s', 32) + "\0", CharactersBaseUrl = "https://characters.example/" };
        Assert.False(options.IsValid());
        options.DedicatedKeySeed = new string('s', 32);
        options.RegistryDockerConfigFile = "relative.json";
        Assert.False(options.IsValid());
        options.RegistryDockerConfigFile = Path.GetTempPath() + "config.json";
        Assert.True(options.IsValid());
    }

    [Fact]
    public void RuntimeManifestDerivesOnlyTheBoundServerKeyAndHardensUnityPod()
    {
        var realm = Fixtures.Realm();
        var server = realm.Profile.Servers[0] with { Transport = "WebSocket", PublicEndpoint = "wss://api.arkya.online/game/beta-web/" };
        var runtime = new RealmRuntimeOptions { DedicatedKeySeed = new string('s', 32),
            CharactersBaseUrl = "http://arkya-characters.arkya-beta.svc.cluster.local:8080/", AllowClusterHttp = true };
        var manifest = KubernetesRealmDeployer.DeploymentManifest(realm, server, runtime);
        var pod = manifest["spec"]!["template"]!["spec"]!;
        var container = pod["containers"]![0]!;
        var environment = container["env"]!.AsArray().ToDictionary(value => value!["name"]!.GetValue<string>(), value => value!["value"]!.GetValue<string>());
        Assert.Equal(RealmRuntimeKeys.DeriveDedicatedKey(runtime.DedicatedKeySeed, realm.RealmId, server.Id), environment["ARKYA_DEDICATED_KEY"]);
        Assert.Equal("1", environment["ARKYA_ALLOW_CLUSTER_HTTP"]);
        Assert.Equal(runtime.CharactersBaseUrl, environment["ARKYA_CHARACTERS_URL"]);
        Assert.DoesNotContain(runtime.DedicatedKeySeed, manifest.ToJsonString());
        Assert.DoesNotContain("DEDICATED_KEY", System.Text.Json.JsonSerializer.Serialize(realm));
        Assert.Equal(1654, pod["securityContext"]!["runAsUser"]!.GetValue<int>());
        Assert.True(container["securityContext"]!["readOnlyRootFilesystem"]!.GetValue<bool>());
        Assert.Equal("6Gi", container["resources"]!["limits"]!["memory"]!.GetValue<string>());
        Assert.Equal("/tmp", container["volumeMounts"]![0]!["mountPath"]!.GetValue<string>());
        var service = KubernetesRealmDeployer.ServiceManifest(realm, server);
        Assert.Equal("ClusterIP", service["spec"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task ReservedTargetCannotBeCreatedUnderAnotherRealmIdentity()
    {
        var options = Fixtures.Options();
        options.Targets[0].RealmId = "beta-web";
        var repository = new MemoryRealmRepository();
        var service = new RealmService(repository, Options.Create(options), new TestClock());
        Assert.Equal("beta-web", Assert.Single(service.GetOptions().Targets).RealmId);
        var invalid = await service.CreateAsync(new CreateRealmRequest("other-realm", "Other", "realm-eu-one", "island-v1"), default);
        Assert.Equal("target_realm_mismatch", invalid.Error);
        Assert.Empty(await repository.ListAsync(default));
        Assert.NotNull((await service.CreateAsync(new("beta-web", "Beta", "realm-eu-one", "island-v1"), default)).Realm);
    }
}
