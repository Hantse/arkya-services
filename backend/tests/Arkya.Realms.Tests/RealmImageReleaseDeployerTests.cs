using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Arkya.Realms.Api.Application;
using Arkya.Realms.Api.Domain;
using Arkya.Realms.Api.Infrastructure;
using Microsoft.Extensions.Options;

namespace Arkya.Realms.Tests;

public sealed class RealmImageReleaseDeployerTests
{
    private const string NextImage = "registry.example/arkya/dedicated@sha256:abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";
    private const string Annotation = "arkya.online/image-release";

    [Fact]
    public async Task ReleasePatchesOnlyImageAndFenceThenWaitsForTheNewObservedGeneration()
    {
        using var cluster = new ReleaseCluster();
        var serviceBefore = cluster.Service.ToJsonString();
        var result = Assert.Single(await cluster.Deployer().ReconcileAsync(cluster.ReleasedRealm, default));
        Assert.Equal(1, cluster.PatchCount);
        Assert.Equal(0, cluster.CreateCount);
        Assert.Equal(0, result.ReadyReplicas);
        Assert.Null(result.ReadyImage);
        Assert.Equal(serviceBefore, cluster.Service.ToJsonString());
        Assert.Equal(NextImage, cluster.Deployment["spec"]!["template"]!["spec"]!["containers"]![0]!["image"]!.GetValue<string>());
        Assert.Equal("Recreate", cluster.Deployment["spec"]!["strategy"]!["type"]!.GetValue<string>());
        Assert.Equal(30, cluster.Deployment["spec"]!["template"]!["spec"]!["terminationGracePeriodSeconds"]!.GetValue<int>());
        Assert.Equal("1", cluster.Deployment["metadata"]!["annotations"]![Annotation]!.GetValue<string>());
        Assert.Equal("preserved", cluster.Deployment["metadata"]!["annotations"]!["external.example/owner"]!.GetValue<string>());
        var patch = Assert.IsType<JsonArray>(cluster.Patch);
        Assert.Equal(new[] { "test", "test", "replace", "add" }, patch.Select(item => item!["op"]!.GetValue<string>()));
        Assert.Equal("/metadata/resourceVersion", patch[0]!["path"]!.GetValue<string>());
        Assert.Equal("/spec/template/spec/containers/0/image", patch[1]!["path"]!.GetValue<string>());
        cluster.Deployment["status"]!["observedGeneration"] = 4;
        var ready = Assert.Single(await cluster.Deployer().ReconcileAsync(cluster.ReleasedRealm, default));
        Assert.Equal(1, ready.ReadyReplicas);
        Assert.Equal(NextImage, ready.ReadyImage);
        Assert.Equal(1, cluster.PatchCount);
        Assert.All(cluster.Methods, method => Assert.True(method == HttpMethod.Get || method == HttpMethod.Patch));
    }

    [Theory]
    [InlineData("labels", "resource_ownership_conflict")]
    [InlineData("name", "resource_ownership_conflict")]
    [InlineData("namespace", "resource_ownership_conflict")]
    [InlineData("image", "resource_configuration_drift")]
    [InlineData("replicas", "resource_configuration_drift")]
    [InlineData("strategy", "resource_configuration_drift")]
    [InlineData("credentials", "resource_configuration_drift")]
    [InlineData("fence", "resource_configuration_drift")]
    public async Task ReleaseRejectsForeignOwnershipUnexpectedDriftAndNewerFencesWithoutMutation(string change, string code)
    {
        using var cluster = new ReleaseCluster();
        if (change == "labels") cluster.Deployment["metadata"]!["labels"]!["arkya.online/deployment-id"] = "foreign";
        if (change == "name") cluster.Deployment["metadata"]!["name"] = "foreign";
        if (change == "namespace") cluster.Deployment["metadata"]!["namespace"] = "foreign";
        if (change == "image") cluster.Deployment["spec"]!["template"]!["spec"]!["containers"]![0]!["image"] = "untrusted/image:latest";
        if (change == "replicas") cluster.Deployment["spec"]!["replicas"] = 2;
        if (change == "strategy") cluster.Deployment["spec"]!["strategy"]!["type"] = "RollingUpdate";
        if (change == "credentials") cluster.Deployment["spec"]!["template"]!["spec"]!["containers"]![0]!["env"]![0]!["value"] = "foreign-realm";
        if (change == "fence") cluster.Deployment["metadata"]!["annotations"]![Annotation] = "3";
        var before = cluster.Deployment.ToJsonString();
        var failure = await Assert.ThrowsAsync<DeploymentFailure>(() => cluster.Deployer().ReconcileAsync(cluster.ReleasedRealm, default));
        Assert.Equal(code, failure.Code);
        Assert.Equal(0, cluster.PatchCount);
        Assert.Equal(0, cluster.CreateCount);
        Assert.Equal(before, cluster.Deployment.ToJsonString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnconfiguredOrContradictoryReleaseNeverGrantsPatchAuthority(bool contradictory)
    {
        using var cluster = new ReleaseCluster();
        var options = cluster.Options();
        if (contradictory) options.Profiles[0].Image = Fixtures.Image;
        else options.ImageReleases = [];
        var failure = await Assert.ThrowsAsync<DeploymentFailure>(() => cluster.Deployer(options).ReconcileAsync(cluster.ReleasedRealm, default));
        Assert.Equal("image_release_conflict", failure.Code);
        Assert.Equal(0, cluster.PatchCount);
        Assert.Equal(0, cluster.CreateCount);
    }

    [Fact]
    public async Task HistoricalDigestRollbackUsesANewerFenceAndCannotBeReplayedByTheOldWorker()
    {
        using var cluster = new ReleaseCluster();
        await cluster.Deployer().ReconcileAsync(cluster.ReleasedRealm, default);
        var rollback = cluster.ReleasedRealm with
        {
            Profile = cluster.ReleasedRealm.Profile with { Image = Fixtures.Image },
            ImageRelease = new(2, NextImage, Fixtures.Image, 1)
        };
        var options = cluster.Options();
        options.Profiles[0].Image = Fixtures.Image;
        options.ImageReleases[0].Version = 2;
        options.ImageReleases[0].PreviousImage = NextImage;
        await cluster.Deployer(options).ReconcileAsync(rollback, default);
        Assert.Equal(2, cluster.PatchCount);
        Assert.Equal("2", cluster.Deployment["metadata"]!["annotations"]![Annotation]!.GetValue<string>());
        var failure = await Assert.ThrowsAsync<DeploymentFailure>(() => cluster.Deployer().ReconcileAsync(cluster.ReleasedRealm, default));
        Assert.Equal("resource_configuration_drift", failure.Code);
        Assert.Equal(2, cluster.PatchCount);
        Assert.Equal(Fixtures.Image, cluster.Deployment["spec"]!["template"]!["spec"]!["containers"]![0]!["image"]!.GetValue<string>());
    }

    [Fact]
    public async Task LostPatchResponseAndRestartReuseTheSameReleaseWithoutAnotherPatch()
    {
        using var cluster = new ReleaseCluster { LosePatchResponse = true };
        await Assert.ThrowsAsync<HttpRequestException>(() => cluster.Deployer().ReconcileAsync(cluster.ReleasedRealm, default));
        Assert.Equal(1, cluster.PatchCount);
        cluster.Deployment["status"]!["observedGeneration"] = 4;
        var resumed = Assert.Single(await cluster.Deployer().ReconcileAsync(cluster.ReleasedRealm, default));
        Assert.Equal(NextImage, resumed.ReadyImage);
        Assert.Equal(1, cluster.PatchCount);
    }

    [Fact]
    public async Task ConcurrentResourceVersionChangeRefusesThePatchAndCanBeRetried()
    {
        using var cluster = new ReleaseCluster { ChangeBeforePatch = true };
        var failure = await Assert.ThrowsAsync<DeploymentFailure>(() => cluster.Deployer().ReconcileAsync(cluster.ReleasedRealm, default));
        Assert.Equal("resource_conflict", failure.Code);
        Assert.Equal(Fixtures.Image, cluster.Deployment["spec"]!["template"]!["spec"]!["containers"]![0]!["image"]!.GetValue<string>());
        cluster.ChangeBeforePatch = false;
        await cluster.Deployer().ReconcileAsync(cluster.ReleasedRealm, default);
        Assert.Equal(2, cluster.PatchCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PatchResponseIsRevalidatedBeforeItCanAffectReadiness(bool foreign)
    {
        using var cluster = new ReleaseCluster { CorruptPatchResponse = foreign ? "labels" : "image" };
        var failure = await Assert.ThrowsAsync<DeploymentFailure>(() => cluster.Deployer().ReconcileAsync(cluster.ReleasedRealm, default));
        Assert.Equal(foreign ? "resource_ownership_conflict" : "resource_configuration_drift", failure.Code);
    }

    private sealed class ReleaseCluster : HttpMessageHandler
    {
        public string TokenFile { get; } = Path.GetTempFileName();
        public HttpClient Client { get; }
        public RealmRecord BaselineRealm { get; }
        public RealmRecord ReleasedRealm { get; }
        public JsonObject Deployment { get; }
        public JsonObject Service { get; }
        public JsonArray? Patch { get; private set; }
        public List<HttpMethod> Methods { get; } = [];
        public int PatchCount { get; private set; }
        public int CreateCount { get; private set; }
        public bool ChangeBeforePatch { get; set; }
        public bool LosePatchResponse { get; init; }
        public string? CorruptPatchResponse { get; init; }

        public ReleaseCluster()
        {
            File.WriteAllText(TokenFile, "synthetic-test-token");
            Client = new HttpClient(this);
            var original = Fixtures.Realm(RealmStatus.Ready);
            var server = original.Profile.Servers[0] with { Transport = "WebSocket", PublicEndpoint = "wss://game.example/eu-one/" };
            BaselineRealm = original with { Profile = original.Profile with { Servers = [server] } };
            ReleasedRealm = BaselineRealm with
            {
                Profile = BaselineRealm.Profile with { Image = NextImage },
                ImageRelease = new(1, Fixtures.Image, NextImage), Status = RealmStatus.Pending
            };
            Deployment = KubernetesRealmDeployer.DeploymentManifest(BaselineRealm, server);
            Deployment["metadata"]!["resourceVersion"] = "10";
            Deployment["metadata"]!["generation"] = 3;
            Deployment["metadata"]!["annotations"] = new JsonObject { ["external.example/owner"] = "preserved" };
            Deployment["status"] = new JsonObject { ["observedGeneration"] = 3, ["availableReplicas"] = 1, ["updatedReplicas"] = 1 };
            Service = KubernetesRealmDeployer.ServiceManifest(BaselineRealm, server);
        }

        public DeploymentOptions Options()
        {
            var options = Fixtures.Options();
            options.TokenFile = TokenFile;
            options.Targets[0].RealmId = BaselineRealm.RealmId;
            options.Profiles[0].Image = NextImage;
            options.Profiles[0].Servers[0].Transport = "WebSocket";
            options.Profiles[0].Servers[0].PublicEndpoint = "wss://game.example/eu-one/";
            options.ImageReleases = [new() { RealmId = BaselineRealm.RealmId, ProfileId = BaselineRealm.Profile.Id,
                Version = 1, PreviousImage = Fixtures.Image }];
            return options;
        }

        public KubernetesRealmDeployer Deployer(DeploymentOptions? options = null) => new(Client, Microsoft.Extensions.Options.Options.Create(options ?? Options()));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Methods.Add(request.Method);
            var expectedName = KubernetesRealmDeployer.ResourceName(BaselineRealm, BaselineRealm.Profile.Servers[0]);
            Assert.EndsWith("/" + expectedName, request.RequestUri!.AbsolutePath);
            Assert.Contains("/namespaces/" + BaselineRealm.Namespace + "/", request.RequestUri.AbsolutePath);
            if (request.Method == HttpMethod.Get)
                return Json(request.RequestUri.AbsolutePath.Contains("/deployments/") ? Deployment : Service);
            if (request.Method == HttpMethod.Post) { CreateCount++; throw new InvalidOperationException("Existing release must not create resources."); }
            Assert.Equal(HttpMethod.Patch, request.Method);
            Assert.Contains("/deployments/", request.RequestUri.AbsolutePath);
            Assert.Equal("application/json-patch+json", request.Content!.Headers.ContentType!.MediaType);
            PatchCount++;
            Patch = JsonNode.Parse(await request.Content.ReadAsStringAsync(cancellationToken))!.AsArray();
            if (ChangeBeforePatch) Deployment["metadata"]!["resourceVersion"] = "11";
            if (!JsonNode.DeepEquals(Patch[0]!["value"], Deployment["metadata"]!["resourceVersion"]) ||
                !JsonNode.DeepEquals(Patch[1]!["value"], Deployment["spec"]!["template"]!["spec"]!["containers"]![0]!["image"]))
                return new(HttpStatusCode.Conflict) { Content = new StringContent("private cluster conflict") };
            Deployment["spec"]!["template"]!["spec"]!["containers"]![0]!["image"] = Patch[2]!["value"]!.DeepClone();
            Deployment["metadata"]!["annotations"] = Patch[3]!["value"]!.DeepClone();
            Deployment["metadata"]!["generation"] = Deployment["metadata"]!["generation"]!.GetValue<int>() + 1;
            Deployment["metadata"]!["resourceVersion"] = (int.Parse(Deployment["metadata"]!["resourceVersion"]!.GetValue<string>()) + 1).ToString();
            if (LosePatchResponse) throw new HttpRequestException("Lost response after atomic write.");
            var response = Deployment.DeepClone().AsObject();
            if (CorruptPatchResponse == "labels") response["metadata"]!["labels"] = new JsonObject();
            if (CorruptPatchResponse == "image") response["spec"]!["template"]!["spec"]!["containers"]![0]!["image"] = Fixtures.Image;
            return Json(response);
        }

        private static HttpResponseMessage Json(JsonObject value) => new(HttpStatusCode.OK)
            { Content = new StringContent(value.ToJsonString(), Encoding.UTF8, "application/json") };

        protected override void Dispose(bool disposing)
        {
            File.Delete(TokenFile);
            base.Dispose(disposing);
        }
    }
}
