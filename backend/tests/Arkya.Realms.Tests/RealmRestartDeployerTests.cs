using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Arkya.Realms.Api.Application;
using Arkya.Realms.Api.Domain;
using Arkya.Realms.Api.Infrastructure;
using Microsoft.Extensions.Options;

namespace Arkya.Realms.Tests;

public sealed class RealmRestartDeployerTests
{
    private const string Annotation = "arkya.online/restart-revision";
    private const string NextImage = "registry.example/arkya/dedicated@sha256:abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";

    [Fact]
    public async Task RestartOnlyPatchesPodAnnotationAndWaitsForObservedGeneration()
    {
        using var cluster = new RestartCluster();
        var serviceBefore = cluster.Service.ToJsonString();
        var imageBefore = cluster.Deployment["spec"]!["template"]!["spec"]!.ToJsonString();
        var result = Assert.Single(await cluster.Deployer().ReconcileAsync(cluster.Requested, default));
        Assert.Equal(1, cluster.PatchCount);
        Assert.Equal(0, result.ReadyReplicas);
        Assert.Null(result.ReadyRestartRevision);
        Assert.Equal(serviceBefore, cluster.Service.ToJsonString());
        Assert.Equal(imageBefore, cluster.Deployment["spec"]!["template"]!["spec"]!.ToJsonString());
        Assert.Equal("2", cluster.Deployment["spec"]!["template"]!["metadata"]!["annotations"]![Annotation]!.GetValue<string>());
        Assert.Equal("keep", cluster.Deployment["spec"]!["template"]!["metadata"]!["annotations"]!["external/owner"]!.GetValue<string>());
        var patch = Assert.IsType<JsonArray>(cluster.LastPatch);
        Assert.Equal(new[] { "test", "add" }, patch.Select(item => item!["op"]!.GetValue<string>()));
        Assert.Equal("/metadata/resourceVersion", patch[0]!["path"]!.GetValue<string>());
        Assert.Equal("/spec/template/metadata/annotations", patch[1]!["path"]!.GetValue<string>());
        Assert.Equal("Recreate", cluster.Deployment["spec"]!["strategy"]!["type"]!.GetValue<string>());
        cluster.MarkReady();
        var ready = Assert.Single(await cluster.Deployer().ReconcileAsync(cluster.Requested, default));
        Assert.Equal(1, ready.ReadyReplicas);
        Assert.Equal(2, ready.ReadyRestartRevision);
        Assert.Equal(1, cluster.PatchCount);
    }

    [Fact]
    public async Task LostRestartResponseIsIdempotentAndOldWorkerCannotReplayAfterNewerRestart()
    {
        using var cluster = new RestartCluster { LosePatchResponse = true };
        await Assert.ThrowsAsync<HttpRequestException>(() => cluster.Deployer().ReconcileAsync(cluster.Requested, default));
        Assert.Equal(1, cluster.PatchCount);
        cluster.MarkReady();
        var resumed = Assert.Single(await cluster.Deployer().ReconcileAsync(cluster.Requested, default));
        Assert.Equal(2, resumed.ReadyRestartRevision);
        Assert.Equal(1, cluster.PatchCount);
        cluster.LosePatchResponse = false;
        var next = cluster.Requested with { Revision = 3, Restart = new(3, 2) };
        await cluster.Deployer().ReconcileAsync(next, default);
        Assert.Equal(2, cluster.PatchCount);
        var failure = await Assert.ThrowsAsync<DeploymentFailure>(() => cluster.Deployer().ReconcileAsync(cluster.Requested, default));
        Assert.Equal("restart_conflict", failure.Code);
        Assert.Equal(2, cluster.PatchCount);
    }

    [Theory]
    [InlineData("labels", "resource_ownership_conflict")]
    [InlineData("image", "resource_configuration_drift")]
    [InlineData("replicas", "resource_configuration_drift")]
    [InlineData("strategy", "resource_configuration_drift")]
    [InlineData("fence", "restart_conflict")]
    public async Task RestartRefusesForeignResourcesAndDriftBeforeMutation(string change, string code)
    {
        using var cluster = new RestartCluster();
        if (change == "labels") cluster.Deployment["metadata"]!["labels"]!["arkya.online/deployment-id"] = "foreign";
        if (change == "image") cluster.Deployment["spec"]!["template"]!["spec"]!["containers"]![0]!["image"] = "unknown/image:latest";
        if (change == "replicas") cluster.Deployment["spec"]!["replicas"] = 2;
        if (change == "strategy") cluster.Deployment["spec"]!["strategy"]!["type"] = "RollingUpdate";
        if (change == "fence") cluster.Deployment["spec"]!["template"]!["metadata"]!["annotations"]![Annotation] = "99";
        var before = cluster.Deployment.ToJsonString();
        Assert.Equal(code, (await Assert.ThrowsAsync<DeploymentFailure>(() => cluster.Deployer().ReconcileAsync(cluster.Requested, default))).Code);
        Assert.Equal(0, cluster.PatchCount);
        Assert.Equal(before, cluster.Deployment.ToJsonString());
    }

    [Fact]
    public async Task ConcurrentClusterChangeRefusesRestartAndRevalidatesBeforeRetry()
    {
        using var cluster = new RestartCluster { ChangeBeforePatch = true };
        Assert.Equal("resource_conflict", (await Assert.ThrowsAsync<DeploymentFailure>(() => cluster.Deployer().ReconcileAsync(cluster.Requested, default))).Code);
        Assert.Null(cluster.Deployment["spec"]!["template"]!["metadata"]!["annotations"]![Annotation]);
        cluster.ChangeBeforePatch = false;
        await cluster.Deployer().ReconcileAsync(cluster.Requested, default);
        Assert.Equal(2, cluster.PatchCount);
    }

    [Fact]
    public async Task RestartAndApprovedImageReleaseKeepTheirIndependentFences()
    {
        using var cluster = new RestartCluster();
        await cluster.Deployer().ReconcileAsync(cluster.Requested, default);
        var released = cluster.Requested with
        {
            Revision = 3, Profile = cluster.Requested.Profile with { Image = NextImage },
            ImageRelease = new(1, Fixtures.Image, NextImage)
        };
        var options = cluster.Options();
        options.Targets[0].RealmId = released.RealmId;
        options.Profiles[0].Image = NextImage;
        options.ImageReleases = [new() { RealmId = released.RealmId, ProfileId = released.Profile.Id, PreviousImage = Fixtures.Image, Version = 1 }];
        await cluster.Deployer(options).ReconcileAsync(released, default);
        Assert.Equal("2", cluster.Deployment["spec"]!["template"]!["metadata"]!["annotations"]![Annotation]!.GetValue<string>());
        options.Profiles[0].Image = Fixtures.Image;
        options.ImageReleases[0].Version = 2;
        options.ImageReleases[0].PreviousImage = NextImage;
        options.ImageReleases[0].Automatic = false;
        var restarted = released with { Revision = 4, Restart = new(4, 2) };
        await cluster.Deployer(options).ReconcileAsync(restarted, default);
        Assert.Equal("4", cluster.Deployment["spec"]!["template"]!["metadata"]!["annotations"]![Annotation]!.GetValue<string>());
        Assert.Equal("1", cluster.Deployment["metadata"]!["annotations"]!["arkya.online/image-release"]!.GetValue<string>());
        Assert.Equal(NextImage, cluster.Deployment["spec"]!["template"]!["spec"]!["containers"]![0]!["image"]!.GetValue<string>());
        Assert.Equal(3, cluster.PatchCount);
    }

    private sealed class RestartCluster : HttpMessageHandler
    {
        private readonly string token = Path.GetTempFileName();
        private readonly HttpClient client;
        public RealmRecord Requested { get; }
        public JsonObject Deployment { get; }
        public JsonObject Service { get; }
        public JsonArray? LastPatch { get; private set; }
        public int PatchCount { get; private set; }
        public bool LosePatchResponse { get; set; }
        public bool ChangeBeforePatch { get; set; }

        public RestartCluster()
        {
            File.WriteAllText(token, "synthetic-test-token");
            client = new(this);
            var baseline = Fixtures.Realm(RealmStatus.Ready);
            Requested = baseline with { Revision = 2, Status = RealmStatus.Pending, Restart = new(2, 0) };
            Deployment = KubernetesRealmDeployer.DeploymentManifest(baseline, baseline.Profile.Servers[0]);
            Deployment["metadata"]!["resourceVersion"] = "10";
            Deployment["metadata"]!["generation"] = 3L;
            Deployment["spec"]!["template"]!["metadata"]!["annotations"] = new JsonObject { ["external/owner"] = "keep" };
            MarkReady();
            Service = KubernetesRealmDeployer.ServiceManifest(baseline, baseline.Profile.Servers[0]);
            Service["status"] = new JsonObject { ["loadBalancer"] = new JsonObject { ["ingress"] = new JsonArray(new JsonObject { ["ip"] = "192.0.2.42" }) } };
        }

        public void MarkReady() => Deployment["status"] = new JsonObject
        {
            ["observedGeneration"] = Deployment["metadata"]!["generation"]!.DeepClone(),
            ["availableReplicas"] = 1, ["updatedReplicas"] = 1
        };

        public DeploymentOptions Options()
        {
            var options = Fixtures.Options();
            options.TokenFile = token;
            return options;
        }

        public KubernetesRealmDeployer Deployer(DeploymentOptions? options = null) => new(client, Microsoft.Extensions.Options.Options.Create(options ?? Options()));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var name = KubernetesRealmDeployer.ResourceName(Requested, Requested.Profile.Servers[0]);
            Assert.EndsWith("/" + name, request.RequestUri!.AbsolutePath);
            Assert.Contains("/namespaces/" + Requested.Namespace + "/", request.RequestUri.AbsolutePath);
            if (request.Method == HttpMethod.Get)
                return Json(request.RequestUri.AbsolutePath.Contains("/deployments/") ? Deployment : Service);
            Assert.Equal(HttpMethod.Patch, request.Method);
            Assert.Contains("/deployments/", request.RequestUri.AbsolutePath);
            Assert.Equal("application/json-patch+json", request.Content!.Headers.ContentType!.MediaType);
            PatchCount++;
            LastPatch = JsonNode.Parse(await request.Content.ReadAsStringAsync(cancellationToken))!.AsArray();
            if (ChangeBeforePatch) Deployment["metadata"]!["resourceVersion"] = "11";
            foreach (var step in LastPatch)
            {
                var parts = step!["path"]!.GetValue<string>().Split('/').Skip(1).ToArray();
                JsonNode parent = Deployment;
                foreach (var part in parts[..^1]) parent = parent is JsonArray array ? array[int.Parse(part)]! : parent[part]!;
                var value = step["value"];
                var namePart = parts[^1];
                if (step["op"]!.GetValue<string>() == "test")
                {
                    if (!JsonNode.DeepEquals(parent[namePart], value)) return new(HttpStatusCode.Conflict);
                }
                else parent[namePart] = value!.DeepClone();
            }
            Deployment["metadata"]!["generation"] = Deployment["metadata"]!["generation"]!.GetValue<long>() + 1;
            Deployment["metadata"]!["resourceVersion"] = (int.Parse(Deployment["metadata"]!["resourceVersion"]!.GetValue<string>()) + 1).ToString();
            if (LosePatchResponse) throw new HttpRequestException("Lost restart response");
            return Json(Deployment);
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
