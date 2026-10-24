using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Arkya.Realms.Api.Application;
using Arkya.Realms.Api.Domain;
using Arkya.Realms.Api.Infrastructure;
using Microsoft.Extensions.Options;

namespace Arkya.Realms.Tests;

public sealed class RealmDialogueActivationDeployerTests
{
    private const string RestartAnnotation = "arkya.online/restart-revision";

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public async Task ExplicitRestartAtomicallyAddsOnlyDialogueEnvironmentAndWaitsForItsGeneration(long previousRestart)
    {
        using var cluster = new DialogueCluster(previousRestart);
        var serviceBefore = cluster.Service.ToJsonString();
        var result = Assert.Single(await cluster.Deployer().ReconcileAsync(cluster.Requested, default));
        Assert.Equal(0, result.ReadyReplicas);
        Assert.Null(result.ReadyRestartRevision);
        Assert.Equal(serviceBefore, cluster.Service.ToJsonString());
        Assert.Equal(new[] { "arkya-dedicated", "arkya-npc-dialogue", "patch" }, cluster.Mutations);
        var patch = Assert.Single(cluster.Patches);
        Assert.Equal(new[] { "test", "add", "add", "add" }, patch.Select(step => step!["op"]!.GetValue<string>()));
        Assert.Equal("/metadata/resourceVersion", patch[0]!["path"]!.GetValue<string>());
        Assert.Equal("10", patch[0]!["value"]!.GetValue<string>());
        Assert.Equal("/spec/template/metadata/annotations", patch[1]!["path"]!.GetValue<string>());
        Assert.All(patch.Skip(2), step => Assert.Equal("/spec/template/spec/containers/0/env/-", step!["path"]!.GetValue<string>()));
        Assert.True(JsonNode.DeepEquals(cluster.DesiredEnvironment(), cluster.Environment));
        Assert.Equal("arkya-npc-dialogue", cluster.Environment.Last()!["valueFrom"]!["secretKeyRef"]!["name"]!.GetValue<string>());
        Assert.DoesNotContain(cluster.Runtime.InferenceServiceKey, cluster.Deployment.ToJsonString());
        Assert.Equal("keep", cluster.Deployment["spec"]!["template"]!["metadata"]!["annotations"]!["external/owner"]!.GetValue<string>());
        cluster.MarkReady();
        var ready = Assert.Single(await cluster.Deployer().ReconcileAsync(cluster.Requested, default));
        Assert.Equal(1, ready.ReadyReplicas);
        Assert.Equal(cluster.Requested.Restart!.Revision, ready.ReadyRestartRevision);
        Assert.Single(cluster.Patches);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("completed")]
    [InlineData("already-applied")]
    public async Task DialogueEnvironmentCannotBeAddedWithoutAPendingUnappliedRestart(string state)
    {
        using var cluster = new DialogueCluster();
        var realm = cluster.Requested;
        if (state == "none") realm = realm with { Restart = null };
        if (state == "completed") realm = realm with { Restart = realm.Restart! with { Completed = true } };
        if (state == "already-applied")
            cluster.Deployment["spec"]!["template"]!["metadata"]!["annotations"]![RestartAnnotation] = realm.Restart!.Revision.ToString();
        var before = cluster.Deployment.ToJsonString();
        var failure = await Assert.ThrowsAsync<DeploymentFailure>(() => cluster.Deployer().ReconcileAsync(realm, default));
        Assert.Equal("resource_configuration_drift", failure.Code);
        Assert.Empty(cluster.Patches);
        Assert.Equal(before, cluster.Deployment.ToJsonString());
    }

    [Theory]
    [InlineData("url-only", "resource_configuration_drift")]
    [InlineData("key-only", "resource_configuration_drift")]
    [InlineData("url-value", "resource_configuration_drift")]
    [InlineData("secret-reference", "resource_configuration_drift")]
    [InlineData("inference-order", "resource_configuration_drift")]
    [InlineData("duplicate", "resource_configuration_drift")]
    [InlineData("extra-environment", "resource_configuration_drift")]
    [InlineData("environment-value", "resource_configuration_drift")]
    [InlineData("environment-shape", "resource_configuration_drift")]
    [InlineData("environment-order", "resource_configuration_drift")]
    [InlineData("image", "resource_configuration_drift")]
    [InlineData("replicas", "resource_configuration_drift")]
    [InlineData("security", "resource_configuration_drift")]
    [InlineData("selector", "resource_configuration_drift")]
    [InlineData("labels", "resource_ownership_conflict")]
    [InlineData("restart", "restart_conflict")]
    [InlineData("resource-version", "resource_configuration_drift")]
    public async Task ActivationRefusesPartialConfigurationAndUnrelatedDrift(string change, string code)
    {
        using var cluster = new DialogueCluster();
        var inference = cluster.DesiredEnvironment().TakeLast(2).Select(node => node!.DeepClone()).ToArray();
        if (change is "url-only" or "key-only") cluster.Environment.Add(inference[change == "url-only" ? 0 : 1]);
        if (change is "url-value" or "secret-reference" or "inference-order" or "duplicate")
        {
            foreach (var variable in change == "inference-order" ? inference.Reverse() : inference) cluster.Environment.Add(variable);
            if (change == "url-value") cluster.Environment[^2]!["value"] = "https://unexpected.example/";
            if (change == "secret-reference") cluster.Environment[^1]!["valueFrom"]!["secretKeyRef"]!["name"] = "unexpected";
            if (change == "duplicate") cluster.Environment.Add(inference[1].DeepClone());
        }
        if (change == "extra-environment") cluster.Environment.Add(new JsonObject { ["name"] = "UNEXPECTED", ["value"] = "1" });
        if (change == "environment-value") cluster.Environment[0]!["value"] = "unexpected";
        if (change == "environment-shape") cluster.Environment[0]!["valueFrom"] = new JsonObject { ["fieldRef"] = new JsonObject { ["fieldPath"] = "metadata.name" } };
        if (change == "environment-order")
        {
            var first = cluster.Environment[0]!.DeepClone();
            cluster.Environment[0] = cluster.Environment[1]!.DeepClone();
            cluster.Environment[1] = first;
        }
        if (change == "image") cluster.Deployment["spec"]!["template"]!["spec"]!["containers"]![0]!["image"] = "unexpected/image:latest";
        if (change == "replicas") cluster.Deployment["spec"]!["replicas"] = 2;
        if (change == "security") cluster.Deployment["spec"]!["template"]!["spec"]!["containers"]![0]!["securityContext"]!["allowPrivilegeEscalation"] = true;
        if (change == "selector") cluster.Deployment["spec"]!["selector"]!["matchLabels"]!["arkya.online/server-id"] = "unexpected";
        if (change == "labels") cluster.Deployment["metadata"]!["labels"]!["arkya.online/deployment-id"] = "foreign";
        if (change == "restart") cluster.Deployment["spec"]!["template"]!["metadata"]!["annotations"]![RestartAnnotation] = "99";
        if (change == "resource-version") cluster.Deployment["metadata"]!["resourceVersion"] = "";
        var before = cluster.Deployment.ToJsonString();
        var failure = await Assert.ThrowsAsync<DeploymentFailure>(() => cluster.Deployer().ReconcileAsync(cluster.Requested, default));
        Assert.Equal(code, failure.Code);
        Assert.Empty(cluster.Patches);
        Assert.Equal(before, cluster.Deployment.ToJsonString());
    }

    [Theory]
    [InlineData("data", "resource_configuration_drift")]
    [InlineData("labels", "resource_ownership_conflict")]
    public async Task ActivationRequiresAnOwnedMatchingDialogueSecretBeforePatching(string change, string code)
    {
        using var cluster = new DialogueCluster();
        await cluster.ProvisionDialogueSecretAsync();
        var secret = cluster.Resources.Values.Single(value => value["metadata"]!["name"]!.GetValue<string>() == "arkya-npc-dialogue");
        if (change == "data") secret["data"]!["service-key"] = "unexpected-private-data";
        if (change == "labels") secret["metadata"]!["labels"]!["arkya.online/deployment-id"] = "foreign";
        var failure = await Assert.ThrowsAsync<DeploymentFailure>(() => cluster.Deployer().ReconcileAsync(cluster.Requested, default));
        Assert.Equal(code, failure.Code);
        Assert.Empty(cluster.Patches);
        Assert.DoesNotContain(cluster.Runtime.InferenceServiceKey, failure.ToString());
        Assert.DoesNotContain("unexpected-private-data", failure.ToString());
    }

    [Fact]
    public async Task ConcurrentOwnershipChangeFailsTheVersionFenceWithoutAddingDialogueEnvironment()
    {
        using var cluster = new DialogueCluster { ChangeBeforePatch = true };
        var before = cluster.Environment.ToJsonString();
        var failure = await Assert.ThrowsAsync<DeploymentFailure>(() => cluster.Deployer().ReconcileAsync(cluster.Requested, default));
        Assert.Equal("resource_conflict", failure.Code);
        Assert.Equal(before, cluster.Environment.ToJsonString());
        Assert.Null(cluster.Deployment["spec"]!["template"]!["metadata"]!["annotations"]![RestartAnnotation]);
        cluster.ChangeBeforePatch = false;
        var retry = await Assert.ThrowsAsync<DeploymentFailure>(() => cluster.Deployer().ReconcileAsync(cluster.Requested, default));
        Assert.Equal("resource_ownership_conflict", retry.Code);
        Assert.Single(cluster.Patches);
    }

    [Fact]
    public async Task LostActivationResponseResumesWithoutDuplicatingEnvironmentOrRestart()
    {
        using var cluster = new DialogueCluster { LosePatchResponse = true };
        await Assert.ThrowsAsync<HttpRequestException>(() => cluster.Deployer().ReconcileAsync(cluster.Requested, default));
        Assert.True(JsonNode.DeepEquals(cluster.DesiredEnvironment(), cluster.Environment));
        cluster.MarkReady();
        var result = Assert.Single(await cluster.Deployer().ReconcileAsync(cluster.Requested, default));
        Assert.Equal(cluster.Requested.Restart!.Revision, result.ReadyRestartRevision);
        Assert.Single(cluster.Patches);
    }

    private sealed class DialogueCluster : HttpMessageHandler
    {
        private readonly string token = Path.GetTempFileName();
        private readonly HttpClient client;
        private readonly string deploymentPath;
        public RealmRecord Requested { get; }
        public RealmRuntimeOptions Runtime { get; } = new()
        {
            DedicatedKeySeed = "synthetic-dedicated-seed-used-only-in-test",
            CharactersBaseUrl = "https://characters.example/"
        };
        public Dictionary<string, JsonObject> Resources { get; } = [];
        public List<JsonArray> Patches { get; } = [];
        public List<string> Mutations { get; } = [];
        public bool ChangeBeforePatch { get; set; }
        public bool LosePatchResponse { get; set; }
        public JsonObject Deployment => Resources[deploymentPath];
        public JsonObject Service { get; }
        public JsonArray Environment => Deployment["spec"]!["template"]!["spec"]!["containers"]![0]!["env"]!.AsArray();

        public DialogueCluster(long previousRestart = 0)
        {
            File.WriteAllText(token, "synthetic-test-token");
            client = new(this);
            var baseline = Fixtures.Realm(RealmStatus.Ready) with
            {
                Revision = previousRestart + 1,
                Restart = previousRestart == 0 ? null : new(previousRestart, 0) { Completed = true }
            };
            Requested = baseline with { Revision = baseline.Revision + 1, Status = RealmStatus.Pending, Restart = new(baseline.Revision + 1, previousRestart) };
            var deployment = KubernetesRealmDeployer.DeploymentManifest(baseline, baseline.Profile.Servers[0], Runtime);
            deploymentPath = $"/apis/apps/v1/namespaces/{baseline.Namespace}/deployments/{KubernetesRealmDeployer.ResourceName(baseline, baseline.Profile.Servers[0])}";
            deployment["metadata"]!["resourceVersion"] = "10";
            deployment["metadata"]!["generation"] = 3L;
            deployment["spec"]!["template"]!["metadata"]!["annotations"] ??= new JsonObject();
            deployment["spec"]!["template"]!["metadata"]!["annotations"]!["external/owner"] = "keep";
            Resources[deploymentPath] = deployment;
            MarkReady();
            Service = KubernetesRealmDeployer.ServiceManifest(baseline, baseline.Profile.Servers[0]);
            Service["status"] = new JsonObject { ["loadBalancer"] = new JsonObject { ["ingress"] = new JsonArray(new JsonObject { ["ip"] = "192.0.2.42" }) } };
            Resources[$"/api/v1/namespaces/{baseline.Namespace}/services/{KubernetesRealmDeployer.ResourceName(baseline, baseline.Profile.Servers[0])}"] = Service;
            Runtime.InferenceBaseUrl = "https://inference.example/";
            Runtime.InferenceServiceKey = "synthetic-inference-key-used-only-in-test";
        }

        public JsonArray DesiredEnvironment() => KubernetesRealmDeployer.DeploymentManifest(Requested, Requested.Profile.Servers[0], Runtime)
            ["spec"]!["template"]!["spec"]!["containers"]![0]!["env"]!.AsArray();

        public KubernetesRealmDeployer Deployer()
        {
            var options = Fixtures.Options();
            options.TokenFile = token;
            return new(client, Microsoft.Extensions.Options.Options.Create(options), Microsoft.Extensions.Options.Options.Create(Runtime));
        }

        public void MarkReady() => Deployment["status"] = new JsonObject
        {
            ["observedGeneration"] = Deployment["metadata"]!["generation"]!.DeepClone(), ["availableReplicas"] = 1, ["updatedReplicas"] = 1
        };

        public async Task ProvisionDialogueSecretAsync()
        {
            // A drifted workload stops reconciliation immediately after the two secrets have been validated.
            var realm = Requested with { Restart = null };
            Assert.Equal("resource_configuration_drift", (await Assert.ThrowsAsync<DeploymentFailure>(() => Deployer().ReconcileAsync(realm, default))).Code);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Assert.Contains("/namespaces/" + Requested.Namespace + "/", path);
            if (request.Method == HttpMethod.Get)
                return Resources.TryGetValue(path, out var resource) ? Json(resource) : new(HttpStatusCode.NotFound);
            if (request.Method == HttpMethod.Post)
            {
                Assert.EndsWith("/secrets", path);
                var secret = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
                var name = secret["metadata"]!["name"]!.GetValue<string>();
                Resources[path + "/" + name] = secret;
                Mutations.Add(name);
                return Json(secret);
            }
            Assert.Equal(HttpMethod.Patch, request.Method);
            Assert.Equal(deploymentPath, path);
            Assert.Equal("application/json-patch+json", request.Content!.Headers.ContentType!.MediaType);
            var patch = JsonNode.Parse(await request.Content.ReadAsStringAsync(cancellationToken))!.AsArray();
            Patches.Add(patch);
            Mutations.Add("patch");
            if (ChangeBeforePatch)
            {
                Deployment["metadata"]!["resourceVersion"] = "11";
                Deployment["metadata"]!["labels"]!["arkya.online/deployment-id"] = "foreign";
            }
            var updated = Deployment.DeepClone().AsObject();
            foreach (var step in patch)
            {
                var parts = step!["path"]!.GetValue<string>().Split('/').Skip(1).ToArray();
                JsonNode parent = updated;
                foreach (var part in parts[..^1]) parent = parent is JsonArray array ? array[int.Parse(part)]! : parent[part]!;
                var value = step["value"];
                var last = parts[^1];
                if (step["op"]!.GetValue<string>() == "test")
                {
                    if (!JsonNode.DeepEquals(parent[last], value)) return new(HttpStatusCode.Conflict);
                }
                else if (parent is JsonArray array && last == "-") array.Add(value!.DeepClone());
                else parent[last] = value!.DeepClone();
            }
            updated["metadata"]!["generation"] = updated["metadata"]!["generation"]!.GetValue<long>() + 1;
            updated["metadata"]!["resourceVersion"] = (int.Parse(updated["metadata"]!["resourceVersion"]!.GetValue<string>()) + 1).ToString();
            Resources[path] = updated;
            if (LosePatchResponse) throw new HttpRequestException("Lost activation response");
            return Json(updated);
        }

        private static HttpResponseMessage Json(JsonObject value) => new(HttpStatusCode.OK)
            { Content = new StringContent(value.ToJsonString(), Encoding.UTF8, "application/json") };

        protected override void Dispose(bool disposing)
        {
            File.Delete(token);
            base.Dispose(disposing);
        }
    }
}
