using System.Text;
using Arkya.Realms.Api.Application;
using Arkya.Realms.Api.Domain;
using Arkya.Realms.Api.Infrastructure;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Options;

namespace Arkya.Realms.Tests;

public sealed class GameEntryTests
{
    [Fact]
    public void OperatorSummaryPreservesWebSocketTransportAndPublicEndpoint()
    {
        var realm = Fixtures.Realm(RealmStatus.Ready);
        var server = realm.Profile.Servers[0] with { Transport = "WebSocket", PublicEndpoint = "wss://game.example/" };
        realm = realm with { Profile = realm.Profile with { Servers = [server] } };
        var summary = Arkya.Realms.Api.Contracts.RealmSummary.From(realm);
        var destination = Assert.Single(summary.Servers);
        Assert.Equal("WebSocket", destination.Transport);
        Assert.Equal("wss://game.example/", destination.PublicEndpoint);
    }

    [Theory]
    [InlineData("wss://game.example/", false, true)]
    [InlineData("ws://game.example/", true, false)]
    [InlineData("ws://localhost:7979/", true, true)]
    [InlineData("ws://localhost:7979/", false, false)]
    [InlineData("wss://game.example/?ticket=secret", false, false)]
    [InlineData("wss://game.example/#secret", false, false)]
    [InlineData("wss://user:pass@game.example/", false, false)]
    public void OnlyApprovedWebSocketEndpointShapesAreAccepted(string endpoint, bool development, bool expected) =>
        Assert.Equal(expected, GameProtocol.ValidEndpoint(endpoint, development));

    [Theory]
    [InlineData("", 124, 125, true)]
    [InlineData("", 125, 126, false)]
    [InlineData("é", 118, 125, true)]
    [InlineData("é", 119, 126, false)]
    [InlineData("%C3%A9", 118, 125, true)]
    [InlineData("%C3%A9", 119, 126, false)]
    public void OperatorDestinationsFitTheEscapedTransportBuffer(string suffix, int padding, int escapedBytes, bool valid)
    {
        var endpoint = "wss://game.example/" + new string('a', padding) + suffix;
        Assert.Equal(escapedBytes, Encoding.UTF8.GetByteCount(new Uri(endpoint).AbsolutePath));
        Assert.Equal(valid, GameProtocol.ValidEndpoint(endpoint));
        Assert.Equal(valid, new DeploymentServer
        {
            Id = "starter-island", Port = 7777, Transport = "WebSocket", PublicEndpoint = endpoint
        }.IsValid());
        Assert.Equal(valid, new StandaloneGameEntry
        {
            RealmId = "beta-web", DisplayName = "Web beta", ServerId = "starter-island", Endpoint = endpoint
        }.IsValid(false));
    }

    [Theory]
    [InlineData(125, true)]
    [InlineData(126, false)]
    public async Task CatalogueOmitsPersistedDestinationsWhosePathExceedsTransportCapacity(int pathBytes, bool visible)
    {
        var repository = new MemoryRealmRepository();
        var realm = Fixtures.Realm(RealmStatus.Ready);
        var server = realm.Profile.Servers[0] with
        {
            Transport = "WebSocket", PublicEndpoint = "wss://game.example/" + new string('a', pathBytes - 1)
        };
        await repository.TryCreateAsync(realm with { Profile = realm.Profile with { Servers = [server] } }, default);
        var service = new GameEntryService(repository, Options.Create(new GameEntryOptions()));
        Assert.Equal(visible ? 1 : 0, (await service.ListAsync(default)).Realms.Length);
    }

    [Fact]
    public async Task CatalogueRequiresReadyDeploymentAndDoesNotLeakUdpDestinations()
    {
        var repository = new MemoryRealmRepository();
        var realm = Fixtures.Realm(RealmStatus.Deploying);
        var server = realm.Profile.Servers[0] with { Transport = "WebSocket", PublicEndpoint = "wss://game.example/" };
        realm = realm with { Profile = realm.Profile with { Servers = [server] } };
        await repository.TryCreateAsync(realm, default);
        var service = new GameEntryService(repository, Options.Create(new GameEntryOptions { ServiceKey = new string('a', 32) }));
        Assert.False(service.Authorize("wrong")); Assert.True(service.Authorize(new string('a', 32)));
        Assert.False(Assert.Single((await service.ListAsync(default)).Realms).Ready);
        await repository.TryReplaceAsync(realm with { Version = 2, Status = RealmStatus.Ready }, 1, default);
        Assert.True(Assert.Single((await service.ListAsync(default)).Realms).Ready);
        var tcp = KubernetesRealmDeployer.ServiceManifest(realm, server);
        Assert.Equal("TCP", tcp["spec"]!["ports"]![0]!["protocol"]!.GetValue<string>());
        var udp = KubernetesRealmDeployer.ServiceManifest(realm, server with { Transport = "Udp", PublicEndpoint = null });
        Assert.Equal("UDP", udp["spec"]!["ports"]![0]!["protocol"]!.GetValue<string>());
        var webContainer = KubernetesRealmDeployer.DeploymentManifest(realm, server)["spec"]!["template"]!["spec"]!["containers"]![0]!;
        Assert.Equal("test", webContainer["readinessProbe"]!["exec"]!["command"]![0]!.GetValue<string>());
        Assert.Equal("/tmp/arkya-ready", webContainer["readinessProbe"]!["exec"]!["command"]![2]!.GetValue<string>());
        Assert.Contains(webContainer["env"]!.AsArray(), value => value?["name"]?.GetValue<string>() == "ARKYA_READY_FILE" && value["value"]?.GetValue<string>() == "/tmp/arkya-ready");
        var udpContainer = KubernetesRealmDeployer.DeploymentManifest(realm, server with { Transport = "Udp", PublicEndpoint = null })["spec"]!["template"]!["spec"]!["containers"]![0]!;
        Assert.Null(udpContainer["readinessProbe"]);
    }
}
