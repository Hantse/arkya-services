using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arkya.Realms.Api.Application;
using Arkya.Realms.Api.Domain;
using Arkya.Realms.Api.Infrastructure;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Options;

namespace Arkya.Realms.Tests;

public sealed class RealmTopologyDeploymentTests
{
    [Fact]
    public async Task UnsupportedTopologyCannotReachKubernetesEvenWhenItWasQueuedDirectly()
    {
        var options = Fixtures.Options();
        var realm = Fixtures.Realm() with
        {
            Profile = RealmTopologyBuilder.Snapshot(options.Profiles[0], "eu-one", new([new("starter-island", 4)]))
        };
        using var cluster = new TopologyCluster();
        using var client = new HttpClient(cluster);
        var deployer = new KubernetesRealmDeployer(client, Options.Create(options));
        var error = await Assert.ThrowsAsync<DeploymentFailure>(() => deployer.ReconcileAsync(realm, default));
        Assert.Equal("topology_runtime_unsupported", error.Code);
        Assert.Equal(0, cluster.RequestCount);
    }

    [Fact]
    public async Task ApprovedTopologyCreatesSeparateAuthoritiesAndServicesWithSpatialBoundsAndIslandScopedNeighbors()
    {
        var options = Fixtures.Options();
        options.Profiles[0].TopologyTemplateServerId = options.Profiles[0].Servers[0].Id;
        var topology = new RealmTopology([new("starter-island", 4), new("future-island", 4)], 1, 1);
        var realm = Fixtures.Realm() with { Profile = RealmTopologyBuilder.Snapshot(options.Profiles[0], "eu-one", topology) };
        var tokenFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tokenFile, "test-token");
            options.TokenFile = tokenFile;
            using var cluster = new TopologyCluster();
            using var client = new HttpClient(cluster);
            var deployer = new KubernetesRealmDeployer(client, Options.Create(options));
            var observations = await deployer.ReconcileAsync(realm, default);
            Assert.Equal(10, observations.Length);
            Assert.All(observations, observation => Assert.Equal(1, observation.ReadyReplicas));
            Assert.Equal(20, cluster.Resources.Count);
            foreach (var server in realm.Profile.Servers)
            {
                var deployment = Assert.Single(cluster.Resources.Values, resource => resource["kind"]!.GetValue<string>() == "Deployment" &&
                    resource["metadata"]!["labels"]!["arkya.online/server-id"]!.GetValue<string>() == server.Id);
                var service = Assert.Single(cluster.Resources.Values, resource => resource["kind"]!.GetValue<string>() == "Service" &&
                    resource["metadata"]!["labels"]!["arkya.online/server-id"]!.GetValue<string>() == server.Id);
                Assert.Equal(1, deployment["spec"]!["replicas"]!.GetValue<int>());
                Assert.Equal("Recreate", deployment["spec"]!["strategy"]!["type"]!.GetValue<string>());
                Assert.True(JsonNode.DeepEquals(deployment["spec"]!["template"]!["metadata"]!["labels"], service["spec"]!["selector"]));
                Assert.Equal(server.Id, service["spec"]!["selector"]!["arkya.online/server-id"]!.GetValue<string>());
                var environment = deployment["spec"]!["template"]!["spec"]!["containers"]![0]!["env"]!.AsArray()
                    .ToDictionary(value => value!["name"]!.GetValue<string>(), value => value!["value"]!.GetValue<string>());
                Assert.Equal(server.Kind, environment["ARKYA_SERVER_KIND"]);
                Assert.Equal(2, JsonNode.Parse(environment["ARKYA_REALM_TOPOLOGY"])!["islands"]!.AsArray().Count);
                if (server.Kind == DedicatedKind.Island)
                {
                    Assert.Equal(server.IslandId, environment["ARKYA_ISLAND_ID"]);
                    var bounds = JsonSerializer.Deserialize<AreaBounds>(environment["ARKYA_AREA_BOUNDS"], new JsonSerializerOptions(JsonSerializerDefaults.Web));
                    Assert.Equal(server.Bounds, bounds);
                    var neighbors = JsonSerializer.Deserialize<string[]>(environment["ARKYA_AREA_NEIGHBORS"])!;
                    Assert.Equal(2, neighbors.Length);
                    Assert.All(neighbors, neighborId => Assert.Equal(server.IslandId, realm.Profile.Servers.Single(other => other.Id == neighborId).IslandId));
                }
                else
                {
                    Assert.False(environment.ContainsKey("ARKYA_ISLAND_ID"));
                    Assert.False(environment.ContainsKey("ARKYA_AREA_BOUNDS"));
                }
            }
            var resourceCount = cluster.Resources.Count;
            await deployer.ReconcileAsync(realm, default);
            Assert.Equal(resourceCount, cluster.Resources.Count);
            Assert.Equal(20, cluster.CreateCount);
        }
        finally { File.Delete(tokenFile); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyAdmissionCannotChooseAnArbitraryPartitionOrFallbackToAStandaloneDestination(bool supported)
    {
        var options = Fixtures.Options();
        if (supported) options.Profiles[0].TopologyTemplateServerId = options.Profiles[0].Servers[0].Id;
        var realm = Fixtures.Realm(RealmStatus.Ready) with
        {
            Profile = RealmTopologyBuilder.Snapshot(options.Profiles[0], "eu-one", new([new("starter-island", 4)], 1, 1))
        };
        var repository = new MemoryRealmRepository();
        await repository.TryCreateAsync(realm, default);
        var service = new GameEntryService(repository, Options.Create(new GameEntryOptions
        {
            ServiceKey = new string('a', 32),
            StandaloneEntries = [new() { RealmId = realm.RealmId, DisplayName = realm.DisplayName,
                ServerId = "old-island", Endpoint = "wss://game.example/", Enabled = true }]
        }));
        Assert.Empty((await service.ListAsync(default)).Realms);
    }

    [Fact]
    public async Task AmbiguousLegacyWebSocketDestinationsAreNotResolvedByArrayOrder()
    {
        var realm = Fixtures.Realm(RealmStatus.Ready);
        var server = realm.Profile.Servers[0] with { Transport = "WebSocket", PublicEndpoint = "wss://game.example/first/" };
        realm = realm with { Profile = realm.Profile with { Servers = [server, server with { Id = "area-east", PublicEndpoint = "wss://game.example/second/" }] } };
        var repository = new MemoryRealmRepository();
        await repository.TryCreateAsync(realm, default);
        var service = new GameEntryService(repository, Options.Create(new GameEntryOptions()));
        Assert.Empty((await service.ListAsync(default)).Realms);
    }

    private sealed class TopologyCluster : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public int CreateCount { get; private set; }
        public Dictionary<string, JsonObject> Resources { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get)
                return Resources.TryGetValue(path, out var existing) ? Response(HttpStatusCode.OK, existing) : new(HttpStatusCode.NotFound);
            Assert.Equal(HttpMethod.Post, request.Method);
            var resource = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
            resource["metadata"]!["generation"] = 1;
            if (resource["kind"]!.GetValue<string>() == "Deployment")
                resource["status"] = new JsonObject { ["observedGeneration"] = 1, ["availableReplicas"] = 1, ["updatedReplicas"] = 1 };
            else resource["status"] = new JsonObject { ["loadBalancer"] = new JsonObject { ["ingress"] = new JsonArray(new JsonObject { ["ip"] = "192.0.2.10" }) } };
            Resources.Add(path + "/" + resource["metadata"]!["name"]!.GetValue<string>(), resource);
            CreateCount++;
            return Response(HttpStatusCode.Created, resource);
        }

        private static HttpResponseMessage Response(HttpStatusCode status, JsonObject resource) => new(status)
        {
            Content = new StringContent(resource.ToJsonString(), Encoding.UTF8, "application/json")
        };
    }
}
