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
    [Theory]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    public async Task CurrentHandoffBuildPlansPoolsButCannotDeployOrAdvertiseThem(int instances, int raids)
    {
        var options = Fixtures.Options();
        options.Profiles[0].TopologyTemplateServerId = options.Profiles[0].Servers[0].Id;
        options.Profiles[0].Servers[0].ProtocolVersion = GameProtocol.HandoffVersion;
        options.Profiles[0].Servers[0].BuildId = GameProtocol.HandoffBuildId;
        var snapshot = RealmTopologyBuilder.Snapshot(options.Profiles[0], "eu-one", new([new("starter-island", 4)], instances, raids));
        Assert.False(snapshot.TopologyRuntimeSupported);
        Assert.Equal(5, snapshot.Servers.Length);
        var realm = Fixtures.Realm(RealmStatus.Draft) with { Profile = snapshot with { TopologyRuntimeSupported = true } };
        Assert.False(RealmTopologyBuilder.SnapshotSupportsRuntime(realm.Profile));
        var repository = new MemoryRealmRepository();
        await repository.TryCreateAsync(realm, default);
        var service = new RealmService(repository, Options.Create(options), new TestClock());
        Assert.Equal("topology_runtime_unsupported", (await service.RequestDeploymentAsync(realm.RealmId, realm.Revision, default)).Error);
        Assert.False(service.GetOperationsOptions(realm with { Status = RealmStatus.Ready }).CanRestart);
        using var cluster = new TopologyCluster();
        using var client = new HttpClient(cluster);
        var deployer = new KubernetesRealmDeployer(client, Options.Create(options));
        var error = await Assert.ThrowsAsync<DeploymentFailure>(() => deployer.ReconcileAsync(realm, default));
        Assert.Equal("topology_runtime_unsupported", error.Code);
        Assert.Equal(0, cluster.RequestCount);
        await repository.TryReplaceAsync(realm with { Status = RealmStatus.Ready, Version = realm.Version + 1 }, realm.Version, default);
        Assert.Empty((await new GameEntryService(repository, Options.Create(new GameEntryOptions())).ListAsync(default)).Realms);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnsupportedTopologyCannotReachKubernetesEvenWhenItWasQueuedDirectly(bool previouslyFlaggedSupported)
    {
        var options = Fixtures.Options();
        var realm = Fixtures.Realm() with
        {
            Profile = RealmTopologyBuilder.Snapshot(options.Profiles[0], "eu-one", new([new("starter-island", 4)]))
                with { TopologyRuntimeSupported = previouslyFlaggedSupported }
        };
        using var cluster = new TopologyCluster();
        using var client = new HttpClient(cluster);
        var deployer = new KubernetesRealmDeployer(client, Options.Create(options));
        var error = await Assert.ThrowsAsync<DeploymentFailure>(() => deployer.ReconcileAsync(realm, default));
        Assert.Equal("topology_runtime_unsupported", error.Code);
        Assert.Equal(0, cluster.RequestCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApprovedTopologyCreatesSeparateAuthoritiesAndServicesWithSpatialBoundsAndIslandScopedNeighbors(bool explicitGrid)
    {
        var options = Fixtures.Options();
        options.Profiles[0].TopologyTemplateServerId = options.Profiles[0].Servers[0].Id;
        options.Profiles[0].Servers[0].ProtocolVersion = GameProtocol.HandoffVersion;
        options.Profiles[0].Servers[0].BuildId = GameProtocol.HandoffBuildId;
        var topology = new RealmTopology([new("starter-island", 4, GridColumns: explicitGrid ? 1 : null, GridRows: explicitGrid ? 4 : null), new("future-island", 4)]);
        var realm = Fixtures.Realm() with { Profile = RealmTopologyBuilder.Snapshot(options.Profiles[0], "eu-one", topology) };
        Assert.Equal(explicitGrid ? new AreaBounds(0, 0, 1000, 250) : new AreaBounds(0, 0, 500, 500),
            realm.Profile.Servers.Single(server => server.Id == "island-starter-island-01").Bounds);
        var tokenFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tokenFile, "test-token");
            options.TokenFile = tokenFile;
            using var cluster = new TopologyCluster();
            using var client = new HttpClient(cluster);
            var deployer = new KubernetesRealmDeployer(client, Options.Create(options));
            var observations = await deployer.ReconcileAsync(realm, default);
            Assert.Equal(8, observations.Length);
            Assert.All(observations, observation => Assert.Equal(1, observation.ReadyReplicas));
            Assert.Equal(16, cluster.Resources.Count);
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
                var islands = JsonNode.Parse(environment["ARKYA_REALM_TOPOLOGY"])!["islands"]!.AsArray();
                Assert.Equal(2, islands.Count);
                var starter = Assert.Single(islands, island => island!["islandId"]!.GetValue<string>() == "starter-island")!;
                Assert.Equal(explicitGrid ? 1 : null, starter["gridColumns"]?.GetValue<int>());
                Assert.Equal(explicitGrid ? 4 : null, starter["gridRows"]?.GetValue<int>());
                if (server.Kind == DedicatedKind.Island)
                {
                    Assert.Equal(server.IslandId, environment["ARKYA_ISLAND_ID"]);
                    var bounds = JsonSerializer.Deserialize<AreaBounds>(environment["ARKYA_AREA_BOUNDS"], new JsonSerializerOptions(JsonSerializerDefaults.Web));
                    Assert.Equal(server.Bounds, bounds);
                    var neighbors = JsonSerializer.Deserialize<string[]>(environment["ARKYA_AREA_NEIGHBORS"])!;
                    Assert.Equal(explicitGrid && server.IslandId == "starter-island" && server.PartitionIndex is 0 or 3 ? 1 : 2, neighbors.Length);
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
            Assert.Equal(16, cluster.CreateCount);
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
