using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Configuration;

namespace Arkya.Gateway.Tests;

public sealed class RealmPartitionProxyTests
{
    private const string LegacyAddress = "http://arkya-beta-web-island.arkya-beta-realm.svc.cluster.local:17943/";
    private static string Server(int index) => $"island-starter-island-{index:D2}";
    private static string Address(int index) => $"http://arkya-beta-web-{index:D2}.arkya-beta-realm.svc.cluster.local:17943/";
    private static string Cluster(int index) => $"realm-partition-beta-web_{Server(index)}";

    [Theory]
    [InlineData("../island")]
    [InlineData("island/{**path}")]
    [InlineData("island?host=attacker")]
    [InlineData("ISLAND")]
    [InlineData("island_other")]
    public void Partition_ids_cannot_inject_routes(string serverId)
    {
        var options = new GatewayOptions { RealmGamePartitions = [new() { RealmId = "beta-web", ServerId = serverId, Address = Address(1) }] };
        Assert.False(options.HasValidRealmGamePartitions());
    }

    [Fact]
    public void Partition_configuration_is_bounded_unique_and_uses_distinct_private_origins()
    {
        var partition = new RealmGamePartition { RealmId = "beta-web", ServerId = Server(1), Address = Address(1) };
        var options = new GatewayOptions { RealmGamePartitions = [partition] };
        Assert.True(options.HasValidRealmGamePartitions());
        options.RealmGamePartitions = [partition, partition];
        Assert.False(options.HasValidRealmGamePartitions());
        options.RealmGamePartitions = [partition, new() { RealmId = "beta-web", ServerId = Server(2), Address = Address(1).TrimEnd('/') }];
        Assert.False(options.HasValidRealmGamePartitions());
        options.RealmGamePartitions = [new() { RealmId = "beta-web", ServerId = Server(1), Address = "http://127.0.0.1:17943/" }];
        Assert.False(options.HasValidRealmGamePartitions());
        options.RealmGamePartitions = new RealmGamePartition[257];
        Assert.False(options.HasValidRealmGamePartitions());
        options.RealmGamePartitions = null!;
        Assert.False(options.HasValidRealmGamePartitions());
    }

    [Fact]
    public async Task Invalid_partition_configuration_prevents_gateway_startup()
    {
        await using var gateway = new PartitionFactory(overrides: new() { ["Gateway:RealmGamePartitions:0:Address"] = "http://attacker.example/" });
        Assert.Throws<OptionsValidationException>(() => gateway.CreateClient());
    }

    [Fact]
    public async Task Configured_partitions_stay_disabled_with_the_game_route_and_preserve_the_legacy_route_when_enabled()
    {
        await using var disabled = new PartitionFactory(overrides: new() { ["Gateway:RealmGameEnabled"] = "false" });
        var off = disabled.Services.GetRequiredService<IProxyConfigProvider>().GetConfig();
        Assert.DoesNotContain(off.Routes, route => route.ClusterId!.StartsWith("realm-partition-", StringComparison.Ordinal));
        await using var enabled = new PartitionFactory();
        var config = enabled.Services.GetRequiredService<IProxyConfigProvider>().GetConfig();
        Assert.Equal(4, config.Routes.Count(route => route.ClusterId!.StartsWith("realm-partition-", StringComparison.Ordinal)));
        var legacy = Assert.Single(config.Routes, route => route.ClusterId == "realm-game");
        Assert.Equal("/game/beta-web/{**path}", legacy.Match.Path);
        Assert.Equal("/game/beta-web", Assert.Single(legacy.Transforms!)["PathRemovePrefix"]);
        for (var index = 1; index <= 4; index++)
        {
            var route = Assert.Single(config.Routes, route => route.ClusterId == Cluster(index));
            Assert.Equal($"/game/beta-web/{Server(index)}/", route.Match.Path);
            Assert.Equal(-10, route.Order);
            Assert.Equal("api.arkya.online", Assert.Single(route.Match.Hosts!));
            Assert.Equal("GET", Assert.Single(route.Match.Methods!));
            Assert.Equal("/", Assert.Single(route.Transforms!)["PathSet"]);
            var cluster = Assert.Single(config.Clusters, cluster => cluster.ClusterId == route.ClusterId);
            Assert.Equal(Address(index), Assert.Single(cluster.Destinations!).Value.Address);
            Assert.Equal(TimeSpan.FromMinutes(2), cluster.HttpRequest!.ActivityTimeout);
        }
    }

    [Fact]
    public async Task Each_partition_reaches_its_approved_destination_instead_of_the_legacy_catchall()
    {
        await using var legacy = await Downstream.StartAsync("legacy");
        await using var first = await Downstream.StartAsync("first");
        await using var second = await Downstream.StartAsync("second");
        await using var gateway = new PartitionFactory(new()
        {
            ["realm-game"] = legacy.Address, [Cluster(1)] = first.Address, [Cluster(2)] = second.Address
        });
        using var client = gateway.CreateClient(new() { BaseAddress = new Uri("https://api.arkya.online") });
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/game/beta-web/{Server(1)}/")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/game/beta-web/{Server(2)}/")).StatusCode);
        Assert.Equal("/", Assert.Single(first.Requests).Path);
        Assert.Equal("/", Assert.Single(second.Requests).Path);
        Assert.Empty(legacy.Requests);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/game/beta-web/")).StatusCode);
        Assert.Equal("/", Assert.Single(legacy.Requests).Path);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/internal/game/topology")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/game/other-realm/{Server(1)}/")).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsync($"/game/beta-web/{Server(1)}/", null)).StatusCode);
        Assert.Single(first.Requests);
        Assert.Single(second.Requests);
    }

    [Theory]
    [InlineData("https")]
    [InlineData("wss")]
    public async Task Partition_websocket_crosses_the_production_gateway_with_a_fixed_private_path(string forwardedScheme)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration["AllowedHosts"] = "*";
        builder.Logging.ClearProviders();
        await using var downstream = builder.Build();
        var paths = new ConcurrentQueue<string>();
        downstream.UseWebSockets();
        downstream.Run(async context =>
        {
            paths.Enqueue(context.Request.Path);
            using var connection = await context.WebSockets.AcceptWebSocketAsync();
            var bytes = new byte[128];
            var message = await connection.ReceiveAsync(bytes.AsMemory(), timeout.Token);
            await connection.SendAsync(bytes.AsMemory(0, message.Count), message.MessageType, true, timeout.Token);
            await connection.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
        });
        await downstream.StartAsync(timeout.Token);
        var destination = downstream.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single() + "/";
        await using var gateway = new PartitionFactory(new() { [Cluster(3)] = destination });
        gateway.UseKestrel(0);
        using var client = gateway.CreateClient();
        var gatewayAddress = gateway.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Host", "api.arkya.online");
        socket.Options.SetRequestHeader("X-Forwarded-Proto", forwardedScheme);
        await socket.ConnectAsync(new Uri(gatewayAddress.Replace("http://", "ws://", StringComparison.Ordinal) + $"/game/beta-web/{Server(3)}/"), timeout.Token);
        var payload = Encoding.UTF8.GetBytes("partition-netcode-packet");
        await socket.SendAsync(payload.AsMemory(), WebSocketMessageType.Binary, true, timeout.Token);
        var response = new byte[128];
        var result = await socket.ReceiveAsync(response.AsMemory(), timeout.Token);
        Assert.Equal(WebSocketMessageType.Binary, result.MessageType);
        Assert.Equal(payload, response[..result.Count]);
        Assert.Equal("/", Assert.Single(paths));
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
    }

    private sealed class PartitionFactory(Dictionary<string, string>? destinations = null,
        Dictionary<string, string?>? overrides = null) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            var settings = new Dictionary<string, string?>
            {
                ["AllowedHosts"] = "api.arkya.online",
                ["Gateway:RealmGameEnabled"] = "true", ["Gateway:RealmGameAddress"] = LegacyAddress,
                ["ProxyTrust:KnownProxies:0"] = "127.0.0.1", ["ProxyTrust:KnownProxies:1"] = "::1",
                ["ReverseProxy:Clusters:identity:Destinations:primary:Address"] = "http://127.0.0.1:1/"
            };
            for (var index = 1; index <= 4; index++)
            {
                var prefix = $"Gateway:RealmGamePartitions:{index - 1}:";
                settings[prefix + "RealmId"] = "beta-web";
                settings[prefix + "ServerId"] = Server(index);
                settings[prefix + "Address"] = Address(index);
            }
            if (overrides is not null)
                foreach (var setting in overrides) settings[setting.Key] = setting.Value;
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
            if (destinations is null) return;
            builder.ConfigureServices(services =>
            {
                var original = services.Single(descriptor => descriptor.ServiceType == typeof(IProxyConfigProvider));
                services.Remove(original);
                services.AddSingleton<IProxyConfigProvider>(provider =>
                {
                    var config = ((IProxyConfigProvider)original.ImplementationFactory!(provider)).GetConfig();
                    return new InMemoryConfigProvider(config.Routes, config.Clusters.Select(cluster =>
                        destinations.TryGetValue(cluster.ClusterId, out var address)
                            ? cluster with { Destinations = new Dictionary<string, DestinationConfig> { ["test"] = new() { Address = address } } }
                            : cluster).ToArray());
                });
            });
        }
    }
}
