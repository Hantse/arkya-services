using System.Text.Json.Nodes;
using Arkya.Realms.Api.Application;
using Arkya.Realms.Api.Contracts;
using Arkya.Realms.Api.Domain;
using Arkya.Realms.Api.Infrastructure;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Options;

namespace Arkya.Realms.Tests;

public sealed class RealmRuntimeTests
{
    [Theory]
    [InlineData("https://inference.example/", false, true)]
    [InlineData("http://arkya-inference.arkya-beta.svc.cluster.local:8080/", true, true)]
    [InlineData("http://arkya-inference.arkya-beta.svc.cluster.local:8080/", false, false)]
    [InlineData("http://other.example/", true, false)]
    [InlineData("https://token@inference.example/", false, false)]
    [InlineData("https://inference.example/path", false, false)]
    [InlineData("https://inference.example/?secret=x", false, false)]
    public void DialogueRuntimeRequiresBoundedSecretAndApprovedOrigin(string address, bool allowHttp, bool valid)
    {
        var options = new RealmRuntimeOptions { DedicatedKeySeed = new string('d', 32),
            CharactersBaseUrl = "https://characters.example/", InferenceBaseUrl = address,
            InferenceServiceKey = new string('i', 32), AllowClusterHttp = allowHttp };
        Assert.Equal(valid, options.IsValid());
        options.InferenceServiceKey = "";
        Assert.False(options.IsValid());
    }

    [Fact]
    public void DialogueRuntimeIsOptInAndSecretNeverAppearsInPodData()
    {
        var realm = Fixtures.Realm();
        var runtime = new RealmRuntimeOptions { DedicatedKeySeed = new string('s', 32), CharactersBaseUrl = "https://characters.example/" };
        var disabled = KubernetesRealmDeployer.DeploymentManifest(realm, realm.Profile.Servers[0], runtime);
        Assert.DoesNotContain("ARKYA_INFERENCE", disabled.ToJsonString());
        runtime.InferenceBaseUrl = "https://inference.example/";
        runtime.InferenceServiceKey = new string('i', 32);
        var manifest = KubernetesRealmDeployer.DeploymentManifest(realm, realm.Profile.Servers[0], runtime);
        var environment = manifest["spec"]!["template"]!["spec"]!["containers"]![0]!["env"]!.AsArray()
            .ToDictionary(value => value!["name"]!.GetValue<string>(), value => value!);
        Assert.Equal(runtime.InferenceBaseUrl, environment["ARKYA_INFERENCE_API_URL"]["value"]!.GetValue<string>());
        Assert.Equal("arkya-npc-dialogue", environment["ARKYA_INFERENCE_SERVICE_KEY"]["valueFrom"]!["secretKeyRef"]!["name"]!.GetValue<string>());
        Assert.DoesNotContain(runtime.InferenceServiceKey, manifest.ToJsonString());
    }

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
    public void RuntimeManifestReferencesOnlyTheBoundServerSecretAndHardensUnityPod()
    {
        var realm = Fixtures.Realm();
        var server = realm.Profile.Servers[0] with { Transport = "WebSocket", PublicEndpoint = "wss://api.arkya.online/game/beta-web/" };
        var runtime = new RealmRuntimeOptions { DedicatedKeySeed = new string('s', 32),
            CharactersBaseUrl = "http://arkya-characters.arkya-beta.svc.cluster.local:8080/", AllowClusterHttp = true };
        var manifest = KubernetesRealmDeployer.DeploymentManifest(realm, server, runtime);
        var pod = manifest["spec"]!["template"]!["spec"]!;
        var container = pod["containers"]![0]!;
        var environment = container["env"]!.AsArray().ToDictionary(value => value!["name"]!.GetValue<string>(), value => value!);
        var dedicatedKey = environment["ARKYA_DEDICATED_KEY"];
        Assert.Null(dedicatedKey["value"]);
        Assert.Equal("arkya-dedicated", dedicatedKey["valueFrom"]!["secretKeyRef"]!["name"]!.GetValue<string>());
        Assert.Equal(server.Id, dedicatedKey["valueFrom"]!["secretKeyRef"]!["key"]!.GetValue<string>());
        Assert.Equal("1", environment["ARKYA_ALLOW_CLUSTER_HTTP"]["value"]!.GetValue<string>());
        Assert.Equal(runtime.CharactersBaseUrl, environment["ARKYA_CHARACTERS_URL"]["value"]!.GetValue<string>());
        Assert.DoesNotContain(runtime.DedicatedKeySeed, manifest.ToJsonString());
        Assert.DoesNotContain(RealmRuntimeKeys.DeriveDedicatedKey(runtime.DedicatedKeySeed, realm.RealmId, server.Id), manifest.ToJsonString());
        Assert.DoesNotContain("DEDICATED_KEY", System.Text.Json.JsonSerializer.Serialize(realm));
        Assert.Equal(1654, pod["securityContext"]!["runAsUser"]!.GetValue<int>());
        Assert.Equal(1654, pod["securityContext"]!["fsGroup"]!.GetValue<int>());
        Assert.Equal("linux", pod["nodeSelector"]!["kubernetes.io/os"]!.GetValue<string>());
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

    [Fact]
    public async Task UnboundTargetCanDeployAnotherRealmWithoutApplyingTheBetaRelease()
    {
        var options = Fixtures.Options();
        options.ImageReleases = [new() { RealmId = "beta-web", ProfileId = "island-v1", Version = 1,
            PreviousImage = "registry.example/arkya/dedicated@sha256:" + new string('a', 64) }];
        Assert.True(options.IsValid());
        var repository = new MemoryRealmRepository();
        var service = new RealmService(repository, Options.Create(options), new TestClock());
        Assert.Null(Assert.Single(service.GetOptions().Targets).RealmId);
        var created = await service.CreateAsync(new("custom-world", "Custom world", "realm-eu-one", "island-v1"), default);
        Assert.Null(created.Error);
        var pending = await service.RequestDeploymentAsync("custom-world", created.Realm!.Revision, default);
        Assert.Null(pending.Error);
        var deployer = new StubDeployer();
        await new RealmReconciler(repository, deployer, Options.Create(options), new TestClock()).ReconcileAsync(pending.Realm!, default);
        var ready = await repository.FindAsync("custom-world", default);
        Assert.Equal(RealmStatus.Ready, ready!.Status);
        Assert.Null(ready.ImageRelease);
        Assert.Equal("realm-eu-one", ready.TargetId);
        Assert.Equal("arkya-realm-eu-one", ready.Namespace);
        Assert.Equal(1, deployer.Calls);
    }
}
