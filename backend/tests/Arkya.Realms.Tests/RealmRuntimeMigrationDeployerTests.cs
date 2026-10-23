using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Arkya.Realms.Api.Application;
using Arkya.Realms.Api.Domain;
using Arkya.Realms.Api.Infrastructure;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Options;

namespace Arkya.Realms.Tests;

public sealed class RealmRuntimeMigrationDeployerTests
{
    [Fact]
    public async Task RetirementWaitsForObservedZeroAuthoritiesBeforeReplacingKeysOrCreatingPartitions()
    {
        using var cluster = new MigrationCluster();
        var oldSecret = cluster.Secret.ToJsonString();
        Assert.Empty(await cluster.Deployer().ReconcileAsync(cluster.Migration, default));
        Assert.Equal(0, cluster.OldDeployment["spec"]!["replicas"]!.GetValue<int>());
        Assert.Equal(1, cluster.RetirementPatches);
        Assert.Equal(0, cluster.SecretPatches);
        Assert.Equal(0, cluster.Creates);
        Assert.Equal(oldSecret, cluster.Secret.ToJsonString());
        Assert.Empty(await cluster.Deployer().ReconcileAsync(cluster.Migration, default));
        Assert.Equal(1, cluster.RetirementPatches);
        Assert.Equal(0, cluster.SecretPatches);
        cluster.MarkStopped();
        cluster.OldDeployment["status"]!["terminatingReplicas"] = 1;
        Assert.Empty(await cluster.Deployer().ReconcileAsync(cluster.Migration, default));
        Assert.Equal(0, cluster.SecretPatches);
        cluster.OldDeployment["status"]!["terminatingReplicas"] = 0;
        var result = await cluster.Deployer().ReconcileAsync(cluster.Migration, default);
        Assert.Equal(4, result.Length);
        Assert.All(result, server => Assert.Equal(1, server.ReadyReplicas));
        Assert.Equal(1, cluster.SecretPatches);
        Assert.Equal(8, cluster.Creates);
        Assert.Equal(cluster.Migration.Profile.Servers.Select(server => server.Id).Order(), cluster.Secret["data"]!.AsObject().Select(pair => pair.Key).Order());
        var patch = cluster.LastSecretPatch!;
        Assert.Equal(new[] { "test", "replace" }, patch.Select(operation => operation!["op"]!.GetValue<string>()));
        Assert.Equal("/metadata/resourceVersion", patch[0]!["path"]!.GetValue<string>());
        Assert.Equal("50", patch[0]!["value"]!.GetValue<string>());
        Assert.Equal("/data", patch[1]!["path"]!.GetValue<string>());
        await cluster.Deployer().ReconcileAsync(cluster.Migration, default);
        Assert.Equal(1, cluster.SecretPatches);
        Assert.Equal(8, cluster.Creates);
    }

    [Theory]
    [InlineData("labels", "resource_ownership_conflict")]
    [InlineData("name", "resource_ownership_conflict")]
    [InlineData("image", "resource_configuration_drift")]
    [InlineData("replicas", "resource_configuration_drift")]
    [InlineData("release", "resource_configuration_drift")]
    [InlineData("restart", "resource_configuration_drift")]
    public async Task ForeignOrChangedPreviousWorkloadNeverGrantsMutationAuthority(string change, string expected)
    {
        using var cluster = new MigrationCluster();
        if (change == "labels") cluster.OldDeployment["metadata"]!["labels"]!["arkya.online/deployment-id"] = "foreign";
        if (change == "name") cluster.OldDeployment["metadata"]!["name"] = "foreign";
        if (change == "image") cluster.OldDeployment["spec"]!["template"]!["spec"]!["containers"]![0]!["image"] = "untrusted/image:latest";
        if (change == "replicas") cluster.OldDeployment["spec"]!["replicas"] = 2;
        if (change == "release") cluster.OldDeployment["metadata"]!["annotations"]!["arkya.online/image-release"] = "99";
        if (change == "restart") cluster.OldDeployment["spec"]!["template"]!["metadata"]!["annotations"]!["arkya.online/restart-revision"] = "99";
        var before = cluster.OldDeployment.ToJsonString();
        Assert.Equal(expected, (await Assert.ThrowsAsync<DeploymentFailure>(() => cluster.Deployer().ReconcileAsync(cluster.Migration, default))).Code);
        Assert.Equal(before, cluster.OldDeployment.ToJsonString());
        Assert.Equal(0, cluster.RetirementPatches + cluster.SecretPatches + cluster.Creates);
    }

    [Theory]
    [InlineData("deployment")]
    [InlineData("secret")]
    public async Task ConcurrentResourceVersionChangesRejectMutationAndAllowARevalidatedRetry(string resource)
    {
        using var cluster = new MigrationCluster { ConflictAt = resource };
        if (resource == "secret") cluster.MarkStopped();
        var failure = await Assert.ThrowsAsync<DeploymentFailure>(() => cluster.Deployer().ReconcileAsync(cluster.Migration, default));
        Assert.Equal("resource_conflict", failure.Code);
        Assert.Equal(0, cluster.Creates);
        Assert.True(cluster.Secret["data"]!.AsObject().ContainsKey("area-west"));
        if (resource == "deployment") Assert.Equal(1, cluster.OldDeployment["spec"]!["replicas"]!.GetValue<int>());
        cluster.ConflictAt = null;
        await cluster.Deployer().ReconcileAsync(cluster.Migration, default);
    }

    [Theory]
    [InlineData("deployment")]
    [InlineData("secret")]
    public async Task LostResponseResumesPersistedChangesWithoutRepeatingTheirPatch(string resource)
    {
        using var cluster = new MigrationCluster { LoseResponseAt = resource };
        if (resource == "secret") cluster.MarkStopped();
        await Assert.ThrowsAsync<HttpRequestException>(() => cluster.Deployer().ReconcileAsync(cluster.Migration, default));
        cluster.LoseResponseAt = null;
        cluster.MarkStopped();
        var result = await cluster.Deployer().ReconcileAsync(cluster.Migration, default);
        Assert.Equal(4, result.Length);
        Assert.Equal(1, resource == "secret" ? cluster.SecretPatches : cluster.RetirementPatches);
        Assert.Equal(8, cluster.Creates);
    }

    [Theory]
    [InlineData("data", "resource_configuration_drift")]
    [InlineData("labels", "resource_ownership_conflict")]
    public async Task UnexpectedSecretContentsArePreservedAndNoPartitionIsStarted(string change, string expected)
    {
        using var cluster = new MigrationCluster();
        cluster.MarkStopped();
        if (change == "data") cluster.Secret["data"]!["area-west"] = Convert.ToBase64String(Encoding.UTF8.GetBytes("foreign-key"));
        if (change == "labels") cluster.Secret["metadata"]!["labels"]!["arkya.online/deployment-id"] = "foreign";
        var before = cluster.Secret.ToJsonString();
        Assert.Equal(expected, (await Assert.ThrowsAsync<DeploymentFailure>(() => cluster.Deployer().ReconcileAsync(cluster.Migration, default))).Code);
        Assert.Equal(before, cluster.Secret.ToJsonString());
        Assert.Equal(0, cluster.SecretPatches + cluster.Creates);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualPodsBlockMigrationWhenTerminatingCountersOrTheirDeploymentAreAbsent(bool missingDeployment)
    {
        using var cluster = new MigrationCluster();
        cluster.MarkStopped();
        cluster.OldDeployment["status"]!.AsObject().Remove("terminatingReplicas");
        cluster.PreviousDeploymentMissing = missingDeployment;
        cluster.PodPages.Add(cluster.PodPage(cluster.PreviousPod(terminating: true)));
        Assert.Empty(await cluster.Deployer().ReconcileAsync(cluster.Migration, default));
        Assert.Equal(0, cluster.SecretPatches + cluster.Creates);
        Assert.Equal(1, cluster.PodLists);
        cluster.PodPages.Clear();
        Assert.Equal(4, (await cluster.Deployer().ReconcileAsync(cluster.Migration, default)).Length);
    }

    [Fact]
    public async Task PodPaginationCannotHideAnOldAuthorityOnALaterPage()
    {
        using var cluster = new MigrationCluster();
        cluster.MarkStopped();
        cluster.PodPages.Add(new JsonObject { ["metadata"] = new JsonObject { ["continue"] = "next/page+token" }, ["items"] = new JsonArray() });
        cluster.PodPages.Add(cluster.PodPage(cluster.PreviousPod(terminating: false)));
        Assert.Empty(await cluster.Deployer().ReconcileAsync(cluster.Migration, default));
        Assert.Equal(2, cluster.PodLists);
        Assert.Equal(0, cluster.SecretPatches + cluster.Creates);
    }

    [Theory]
    [InlineData("ownership", "resource_ownership_conflict")]
    [InlineData("missing-items", "cluster_response_invalid")]
    [InlineData("bad-token", "cluster_response_invalid")]
    [InlineData("unbounded-pages", "cluster_response_invalid")]
    public async Task UntrustedOrIncompletePodListsDoNotAuthorizeMigration(string change, string expected)
    {
        using var cluster = new MigrationCluster();
        cluster.MarkStopped();
        var page = cluster.PodPage();
        if (change == "ownership")
        {
            var pod = cluster.PreviousPod(terminating: true);
            pod["metadata"]!["labels"]!["arkya.online/deployment-id"] = "foreign";
            page = cluster.PodPage(pod);
        }
        if (change == "missing-items") page.Remove("items");
        if (change == "bad-token") page["metadata"]!["continue"] = 123;
        if (change == "unbounded-pages") page["metadata"]!["continue"] = "again";
        cluster.PodPages.Add(page);
        Assert.Equal(expected, (await Assert.ThrowsAsync<DeploymentFailure>(() => cluster.Deployer().ReconcileAsync(cluster.Migration, default))).Code);
        Assert.Equal(0, cluster.SecretPatches + cluster.Creates);
    }

    private sealed class MigrationCluster : HttpMessageHandler
    {
        private const string Seed = "synthetic-runtime-migration-seed-1234567890";
        private readonly string tokenFile = Path.GetTempFileName();
        private readonly HttpClient client;
        private readonly DeploymentOptions options;
        private readonly RealmRuntimeOptions runtime = new() { DedicatedKeySeed = Seed, CharactersBaseUrl = "https://characters.example" };
        private readonly Dictionary<string, JsonObject> resources = [];
        private readonly string oldPath;
        private readonly string secretPath;
        public RealmRecord Migration { get; }
        public JsonObject OldDeployment => resources[oldPath];
        public JsonObject Secret => resources[secretPath];
        public int RetirementPatches { get; private set; }
        public int SecretPatches { get; private set; }
        public int Creates { get; private set; }
        public string? ConflictAt { get; set; }
        public string? LoseResponseAt { get; set; }
        public JsonArray? LastSecretPatch { get; private set; }
        public List<JsonObject> PodPages { get; } = [];
        public int PodLists { get; private set; }
        public bool PreviousDeploymentMissing { get; set; }

        public MigrationCluster()
        {
            File.WriteAllText(tokenFile, "synthetic-test-token");
            client = new HttpClient(this);
            options = Fixtures.Options(); options.TokenFile = tokenFile;
            options.Targets[0].RealmId = "eu-one";
            var previous = Fixtures.Realm(RealmStatus.Ready) with
            {
                Revision = 2,
                ImageRelease = new(3, "registry.example/arkya/dedicated@sha256:" + new string('b', 64), Fixtures.Image),
                Restart = new(2, 1) { Completed = true }
            };
            var profile = options.Profiles[0];
            profile.Id = "island-v2"; profile.Image = "registry.example/arkya/dedicated@sha256:" + new string('a', 64);
            profile.TopologyTemplateServerId = "island-template";
            profile.TopologyPublicEndpointTemplate = "wss://game.example/{realmId}/{serverId}/";
            profile.Servers = [new() { Id = "island-template", Port = 17943, Transport = "WebSocket", PublicEndpoint = "wss://game.example/template/",
                ProtocolVersion = GameProtocol.HandoffVersion, BuildId = GameProtocol.HandoffBuildId }];
            var topology = new RealmTopology([new("starter-island", 4, 1536, 1536)]);
            options.RuntimeReleases = [new() { RealmId = previous.RealmId, ProfileId = profile.Id, Version = 1,
                PreviousProfileSha256 = RealmRuntimeReleases.Fingerprint(previous.Profile), Topology = topology, Automatic = true }];
            Migration = previous with
            {
                Profile = RealmTopologyBuilder.Snapshot(profile, previous.RealmId, topology), Revision = 3, Status = RealmStatus.Pending,
                ImageRelease = null, Restart = null, RuntimeRelease = new(1, previous.Profile, previous.ImageRelease, previous.Restart)
            };
            var server = previous.Profile.Servers[0];
            oldPath = $"/apis/apps/v1/namespaces/{previous.Namespace}/deployments/{KubernetesRealmDeployer.ResourceName(previous, server)}";
            var deployment = KubernetesRealmDeployer.DeploymentManifest(previous, server, runtime);
            deployment["metadata"]!["resourceVersion"] = "10"; deployment["metadata"]!["generation"] = 2;
            deployment["status"] = new JsonObject { ["observedGeneration"] = 2, ["replicas"] = 1, ["readyReplicas"] = 1,
                ["availableReplicas"] = 1, ["updatedReplicas"] = 1 };
            resources[oldPath] = deployment;
            secretPath = $"/api/v1/namespaces/{previous.Namespace}/secrets/arkya-dedicated";
            resources[secretPath] = new JsonObject
            {
                ["metadata"] = new JsonObject { ["name"] = "arkya-dedicated", ["namespace"] = previous.Namespace, ["resourceVersion"] = "50",
                    ["labels"] = new JsonObject { ["app.kubernetes.io/managed-by"] = "arkya-realms", ["arkya.online/realm-id"] = previous.RealmId,
                        ["arkya.online/deployment-id"] = previous.DeploymentIdentity } },
                ["type"] = "Opaque", ["data"] = new JsonObject { [server.Id] = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                    RealmRuntimeKeys.DeriveDedicatedKey(Seed, previous.RealmId, server.Id))) }
            };
        }

        public KubernetesRealmDeployer Deployer() => new(client, Options.Create(options), Options.Create(runtime));

        public void MarkStopped()
        {
            OldDeployment["spec"]!["replicas"] = 0;
            OldDeployment["status"] = new JsonObject { ["observedGeneration"] = OldDeployment["metadata"]!["generation"]!.DeepClone(),
                ["replicas"] = 0, ["readyReplicas"] = 0, ["availableReplicas"] = 0, ["updatedReplicas"] = 0, ["terminatingReplicas"] = 0 };
        }

        public JsonObject PreviousPod(bool terminating) => new()
        {
            ["metadata"] = new JsonObject { ["name"] = "old-authority-pod", ["namespace"] = Migration.Namespace,
                ["labels"] = OldDeployment["metadata"]!["labels"]!.DeepClone(),
                ["deletionTimestamp"] = terminating ? "2026-10-23T10:00:00Z" : null },
            ["status"] = new JsonObject { ["phase"] = "Running" }
        };

        public JsonObject PodPage(params JsonObject[] pods) => new()
        {
            ["metadata"] = new JsonObject(), ["items"] = new JsonArray(pods.Select(pod => (JsonNode)pod).ToArray())
        };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Assert.Contains("/namespaces/" + Migration.Namespace + "/", path);
            if (path.EndsWith("/pods", StringComparison.Ordinal))
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                var query = Uri.UnescapeDataString(request.RequestUri.Query);
                Assert.Contains("limit=100", query);
                Assert.Contains("app.kubernetes.io/managed-by=arkya-realms", query);
                Assert.Contains("arkya.online/realm-id=" + Migration.RealmId, query);
                Assert.Contains("arkya.online/server-id=area-west", query);
                Assert.Contains("arkya.online/deployment-id=" + Migration.DeploymentIdentity, query);
                var page = query.Contains("continue=", StringComparison.Ordinal) ? Math.Min(PodLists, PodPages.Count - 1) : 0;
                PodLists++;
                return Json(PodPages.Count == 0 ? PodPage() : PodPages[page]);
            }
            if (path == oldPath && PreviousDeploymentMissing) return new(HttpStatusCode.NotFound);
            if (request.Method == HttpMethod.Get) return resources.TryGetValue(path, out var found) ? Json(found) : new(HttpStatusCode.NotFound);
            if (request.Method == HttpMethod.Post)
            {
                Assert.Equal(0, OldDeployment["spec"]!["replicas"]!.GetValue<int>());
                Assert.Equal(4, Secret["data"]!.AsObject().Count);
                Creates++;
                var value = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
                value["metadata"]!["generation"] = 1; value["metadata"]!["resourceVersion"] = "1";
                if (path.Contains("/deployments")) value["status"] = new JsonObject { ["observedGeneration"] = 1, ["availableReplicas"] = 1, ["updatedReplicas"] = 1 };
                resources[path + "/" + value["metadata"]!["name"]!.GetValue<string>()] = value;
                return Json(value);
            }
            Assert.Equal(HttpMethod.Patch, request.Method);
            Assert.Equal("application/json-patch+json", request.Content!.Headers.ContentType!.MediaType);
            var target = resources[path];
            var kind = path == oldPath ? "deployment" : "secret";
            if (kind == "deployment") RetirementPatches++; else SecretPatches++;
            var patch = JsonNode.Parse(await request.Content.ReadAsStringAsync(cancellationToken))!.AsArray();
            if (kind == "secret") LastSecretPatch = patch;
            if (ConflictAt == kind) target["metadata"]!["resourceVersion"] = "999";
            if (!JsonNode.DeepEquals(patch[0]!["value"], target["metadata"]!["resourceVersion"])) return new(HttpStatusCode.Conflict);
            if (kind == "deployment")
            {
                Assert.Equal(1, patch[1]!["value"]!.GetValue<int>());
                Assert.Equal("/spec/replicas", patch[1]!["path"]!.GetValue<string>());
                target["spec"]!["replicas"] = patch[2]!["value"]!.DeepClone();
                target["metadata"]!["generation"] = target["metadata"]!["generation"]!.GetValue<int>() + 1;
            }
            else target["data"] = patch[1]!["value"]!.DeepClone();
            target["metadata"]!["resourceVersion"] = (int.Parse(target["metadata"]!["resourceVersion"]!.GetValue<string>()) + 1).ToString();
            if (LoseResponseAt == kind) throw new HttpRequestException("Synthetic response loss after atomic write.");
            return Json(target);
        }

        private static HttpResponseMessage Json(JsonObject value) => new(HttpStatusCode.OK)
            { Content = new StringContent(value.ToJsonString(), Encoding.UTF8, "application/json") };

        protected override void Dispose(bool disposing) { File.Delete(tokenFile); base.Dispose(disposing); }
    }
}
