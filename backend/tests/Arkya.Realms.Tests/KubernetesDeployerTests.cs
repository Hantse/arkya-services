using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Nodes;
using Arkya.Realms.Api.Application;
using Arkya.Realms.Api.Domain;
using Arkya.Realms.Api.Infrastructure;
using Microsoft.Extensions.Options;

namespace Arkya.Realms.Tests;

public sealed class KubernetesDeployerTests
{
    [Fact]
    public async Task RestartReusesOwnedResourcesAndReReadsRotatedToken()
    {
        using var cluster = new FakeCluster();
        var realm = Fixtures.Realm();
        var deployer = cluster.Deployer();
        var initial = await deployer.ReconcileAsync(realm, default);
        Assert.Equal(0, Assert.Single(initial).ReadyReplicas);
        Assert.Null(initial[0].Address);
        Assert.Equal(2, cluster.CreateCount);
        var deployment = cluster.Resources.Values.Single(value => value["kind"]!.GetValue<string>() == "Deployment");
        deployment["metadata"]!["generation"] = 2;
        deployment["status"] = new JsonObject { ["observedGeneration"] = 1, ["availableReplicas"] = 1, ["updatedReplicas"] = 1 };
        var service = cluster.Resources.Values.Single(value => value["kind"]!.GetValue<string>() == "Service");
        service["status"] = new JsonObject { ["loadBalancer"] = new JsonObject { ["ingress"] = new JsonArray(new JsonObject { ["ip"] = "192.0.2.42" }) } };
        await File.WriteAllTextAsync(cluster.TokenFile, "rotated-token");
        var restarted = cluster.Deployer();
        var staleGeneration = await restarted.ReconcileAsync(realm, default);
        Assert.Equal(0, staleGeneration[0].ReadyReplicas);
        deployment["status"]!["observedGeneration"] = 2;
        var ready = await restarted.ReconcileAsync(realm, default);
        Assert.Equal(1, ready[0].ReadyReplicas);
        Assert.Equal("192.0.2.42", ready[0].Address);
        Assert.Equal(2, cluster.CreateCount);
        Assert.Equal("rotated-token", cluster.Tokens.Last());
        Assert.All(cluster.Methods, method => Assert.True(method == HttpMethod.Get || method == HttpMethod.Post));
    }

    [Fact]
    public async Task NeverOverwritesForeignResourceEvenWithSameRealmAndServerLabels()
    {
        using var cluster = new FakeCluster();
        var realm = Fixtures.Realm();
        var foreign = KubernetesRealmDeployer.DeploymentManifest(realm, realm.Profile.Servers[0]);
        foreign["metadata"]!["labels"]!["arkya.online/deployment-id"] = "foreign-durable-identity";
        cluster.Add(foreign);
        var error = await Assert.ThrowsAsync<DeploymentFailure>(() => cluster.Deployer().ReconcileAsync(realm, default));
        Assert.Equal("resource_ownership_conflict", error.Code);
        Assert.Equal(0, cluster.CreateCount);
    }

    [Fact]
    public async Task CreateConflictIsReadBackAndOwnershipChecked()
    {
        using var cluster = new FakeCluster { ConflictOnCreate = true };
        var result = await cluster.Deployer().ReconcileAsync(Fixtures.Realm(), default);
        Assert.Single(result);
        Assert.Equal(2, cluster.CreateCount);
        using var hostile = new FakeCluster { ConflictOnCreate = true, ForeignConflict = true };
        var error = await Assert.ThrowsAsync<DeploymentFailure>(() => hostile.Deployer().ReconcileAsync(Fixtures.Realm(), default));
        Assert.Equal("resource_ownership_conflict", error.Code);
    }

    [Fact]
    public async Task OwnedResourceWithDriftIsRejectedWithoutUpdatingIt()
    {
        using var cluster = new FakeCluster();
        var realm = Fixtures.Realm();
        var changed = KubernetesRealmDeployer.DeploymentManifest(realm, realm.Profile.Servers[0]);
        changed["spec"]!["template"]!["spec"]!["containers"]![0]!["image"] = "foreign/image:latest";
        cluster.Add(changed);
        var error = await Assert.ThrowsAsync<DeploymentFailure>(() => cluster.Deployer().ReconcileAsync(realm, default));
        Assert.Equal("resource_configuration_drift", error.Code);
        Assert.Equal(0, cluster.CreateCount);
    }

    [Fact]
    public async Task RemovedTargetNeverTouchesKubernetesAndErrorBodiesAreDiscarded()
    {
        using var cluster = new FakeCluster();
        var options = cluster.Options();
        options.Targets = [];
        var deployer = new KubernetesRealmDeployer(cluster.Client, Microsoft.Extensions.Options.Options.Create(options));
        Assert.Equal("target_unavailable", (await Assert.ThrowsAsync<DeploymentFailure>(() => deployer.ReconcileAsync(Fixtures.Realm(), default))).Code);
        Assert.Empty(cluster.Methods);
        cluster.Error = HttpStatusCode.Forbidden;
        var error = await Assert.ThrowsAsync<DeploymentFailure>(() => cluster.Deployer().ReconcileAsync(Fixtures.Realm(), default));
        Assert.Equal("cluster_access_denied", error.Code);
        Assert.DoesNotContain("sensitive", error.ToString());
    }

    [Fact]
    public void ManifestLimitsAuthorityAndSeparatesEachUdpProcess()
    {
        var realm = Fixtures.Realm();
        var server = realm.Profile.Servers[0];
        var deployment = KubernetesRealmDeployer.DeploymentManifest(realm, server);
        var pod = deployment["spec"]!["template"]!["spec"]!;
        Assert.False(pod["automountServiceAccountToken"]!.GetValue<bool>());
        Assert.Equal("Recreate", deployment["spec"]!["strategy"]!["type"]!.GetValue<string>());
        Assert.Equal(1, deployment["spec"]!["replicas"]!.GetValue<int>());
        Assert.Equal("2Gi", pod["containers"]![0]!["resources"]!["limits"]!["memory"]!.GetValue<string>());
        var service = KubernetesRealmDeployer.ServiceManifest(realm, server);
        Assert.Equal("LoadBalancer", service["spec"]!["type"]!.GetValue<string>());
        Assert.Equal("UDP", service["spec"]!["ports"]![0]!["protocol"]!.GetValue<string>());
        Assert.Equal(server.Port, service["spec"]!["ports"]![0]!["port"]!.GetValue<int>());
        var longest = realm with { RealmId = new string('a', 31) };
        Assert.True(KubernetesRealmDeployer.ResourceName(longest, server with { Id = new string('b', 31) }).Length <= 63);
        Assert.NotEqual(KubernetesRealmDeployer.ResourceName(longest, server with { Id = new string('b', 30) + "a" }),
            KubernetesRealmDeployer.ResourceName(longest, server with { Id = new string('b', 30) + "b" }));
    }

    [Fact]
    public void ClusterTrustUsesOnlyConfiguredCaAndPreservesHostnameValidation()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Test CA", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, certificate.ExportCertificatePem());
            var options = Fixtures.Options();
            options.CertificateAuthorityFile = path;
            using var handler = KubernetesTransport.CreateHandler(options);
            Assert.False(handler.AllowAutoRedirect);
            Assert.Null(handler.SslOptions.RemoteCertificateValidationCallback);
            Assert.Equal(X509ChainTrustMode.CustomRootTrust, handler.SslOptions.CertificateChainPolicy!.TrustMode);
            Assert.Single(handler.SslOptions.CertificateChainPolicy.CustomTrustStore);
            Assert.True(handler.SslOptions.CertificateChainPolicy.DisableCertificateDownloads);
        }
        finally { File.Delete(path); }
    }

    private sealed class FakeCluster : HttpMessageHandler, IDisposable
    {
        public string TokenFile { get; } = Path.GetTempFileName();
        public HttpClient Client { get; }
        public Dictionary<string, JsonObject> Resources { get; } = [];
        public List<HttpMethod> Methods { get; } = [];
        public List<string> Tokens { get; } = [];
        public int CreateCount { get; private set; }
        public bool ConflictOnCreate { get; init; }
        public bool ForeignConflict { get; init; }
        public HttpStatusCode? Error { get; set; }
        public FakeCluster()
        {
            File.WriteAllText(TokenFile, "initial-token");
            Client = new HttpClient(this);
        }
        public DeploymentOptions Options()
        {
            var options = Fixtures.Options();
            options.TokenFile = TokenFile;
            return options;
        }
        public KubernetesRealmDeployer Deployer() => new(Client, Microsoft.Extensions.Options.Options.Create(Options()));
        public void Add(JsonObject resource)
        {
            var api = resource["kind"]!.GetValue<string>() == "Deployment" ? "/apis/apps/v1" : "/api/v1";
            var type = resource["kind"]!.GetValue<string>() == "Deployment" ? "deployments" : "services";
            Resources[$"{api}/namespaces/{resource["metadata"]!["namespace"]!.GetValue<string>()}/{type}/{resource["metadata"]!["name"]!.GetValue<string>()}"] = resource;
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Methods.Add(request.Method);
            Tokens.Add(request.Headers.Authorization!.Parameter!);
            if (Error is { } error) return new(error) { Content = new StringContent("sensitive raw cluster error") };
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get)
                return Resources.TryGetValue(path, out var resource) ? Json(HttpStatusCode.OK, resource) : new(HttpStatusCode.NotFound);
            if (request.Method != HttpMethod.Post) throw new InvalidOperationException("Only GET/POST expected.");
            CreateCount++;
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
            if (ForeignConflict) body["metadata"]!["labels"]!["arkya.online/deployment-id"] = "foreign";
            Add(body);
            return ConflictOnCreate ? new(HttpStatusCode.Conflict) : Json(HttpStatusCode.Created, body);
        }
        private static HttpResponseMessage Json(HttpStatusCode status, JsonObject value) => new(status)
        { Content = new StringContent(value.ToJsonString(), Encoding.UTF8, "application/json") };
        protected override void Dispose(bool disposing)
        {
            File.Delete(TokenFile);
            base.Dispose(disposing);
        }
    }
}
