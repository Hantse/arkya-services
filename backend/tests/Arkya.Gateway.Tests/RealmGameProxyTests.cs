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
using Yarp.ReverseProxy.Configuration;

namespace Arkya.Gateway.Tests;

public sealed class RealmGameProxyTests
{
    private const string RealmAddress = "http://arkya-beta-web-island.arkya-beta-realm.svc.cluster.local:17943/";

    [Theory]
    [InlineData(RealmAddress, true)]
    [InlineData("http://arkya-beta-web-island.arkya-beta-realm.svc.cluster.local:17943", true)]
    [InlineData("https://arkya-beta-web-island.arkya-beta-realm.svc.cluster.local:17943/", false)]
    [InlineData("http://localhost:17943/", false)]
    [InlineData("http://127.0.0.1:17943/", false)]
    [InlineData("http://192.168.100.218:17943/", false)]
    [InlineData("http://game.example/", false)]
    [InlineData("http://arkya-beta-web-island.arkya-beta-realm.svc.cluster.local.attacker/", false)]
    [InlineData("http://user:password@arkya-beta-web-island.arkya-beta-realm.svc.cluster.local/", false)]
    [InlineData(RealmAddress + "game/", false)]
    [InlineData(RealmAddress + "?secret=value", false)]
    [InlineData(RealmAddress + "#fragment", false)]
    public void Dedicated_destination_requires_an_exact_private_cluster_http_origin(string address, bool expected)
    {
        Assert.Equal(expected, GatewayOptions.IsRealmGameOriginValid(address));
    }

    [Fact]
    public async Task Dedicated_game_route_is_disabled_by_default()
    {
        Assert.False(new GatewayOptions().RealmGameEnabled);
        await using var identity = await Downstream.StartAsync("identity");
        await using var factory = new GatewayFactory(identity.Address, overrides: new()
        {
            ["Gateway:RealmGameAddress"] = RealmAddress
        });
        using var client = factory.Client();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/game/beta-web/")).StatusCode);
        Assert.Empty(identity.Requests);
        Assert.DoesNotContain(factory.Services.GetRequiredService<IProxyConfigProvider>().GetConfig().Routes,
            route => route.ClusterId == "realm-game");
    }

    [Fact]
    public async Task Enabled_route_has_only_the_dedicated_destination_and_removes_the_public_prefix()
    {
        await using var factory = new RealmGameFactory();
        var config = factory.Services.GetRequiredService<IProxyConfigProvider>().GetConfig();
        var route = Assert.Single(config.Routes, route => route.ClusterId == "realm-game");
        Assert.Equal("/game/beta-web/{**path}", route.Match.Path);
        Assert.Equal("api.arkya.online", Assert.Single(route.Match.Hosts!));
        Assert.Equal("GET", Assert.Single(route.Match.Methods!));
        Assert.Equal("/game/beta-web", Assert.Single(route.Transforms!)["PathRemovePrefix"]);
        var cluster = Assert.Single(config.Clusters, cluster => cluster.ClusterId == "realm-game");
        Assert.Equal(RealmAddress, Assert.Single(cluster.Destinations!).Value.Address);
        Assert.Equal(TimeSpan.FromMinutes(2), cluster.HttpRequest!.ActivityTimeout);
    }

    [Theory]
    [InlineData("/internal/game/admissions/consume")]
    [InlineData("/internal/characters/11111111-1111-1111-1111-111111111111/resolve")]
    [InlineData("/internal/realms/entries")]
    [InlineData("/game/other-realm/")]
    public async Task Enabled_game_route_does_not_publish_private_service_routes_or_other_realms(string path)
    {
        await using var factory = new RealmGameFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://api.arkya.online") });
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task Websocket_upgrade_and_binary_game_messages_cross_the_gateway_with_the_prefix_removed()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration["AllowedHosts"] = "*";
        builder.Logging.ClearProviders();
        await using var downstream = builder.Build();
        var received = new ConcurrentQueue<string>();
        downstream.UseWebSockets();
        downstream.Run(async context =>
        {
            received.Enqueue(context.Request.Path);
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = 400;
                return;
            }
            using var connection = await context.WebSockets.AcceptWebSocketAsync();
            var buffer = new byte[128];
            var message = await connection.ReceiveAsync(buffer.AsMemory(), timeout.Token);
            await connection.SendAsync(buffer.AsMemory(0, message.Count), message.MessageType, message.EndOfMessage, timeout.Token);
            await connection.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
        });
        await downstream.StartAsync(timeout.Token);
        var address = downstream.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        var gatewayBuilder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        gatewayBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        gatewayBuilder.Configuration.AddInMemoryCollection(Settings());
        gatewayBuilder.Logging.ClearProviders();
        gatewayBuilder.Services.AddGateway();
        UseTestRealmDestination(gatewayBuilder.Services, address + "/");
        await using var gateway = gatewayBuilder.Build();
        gateway.MapReverseProxy();
        await gateway.StartAsync(timeout.Token);
        var gatewayAddress = gateway.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Host", "api.arkya.online");
        await socket.ConnectAsync(new Uri(gatewayAddress.Replace("http://", "ws://", StringComparison.Ordinal) + "/game/beta-web/"), timeout.Token);
        var payload = Encoding.UTF8.GetBytes("netcode-game-packet");
        await socket.SendAsync(payload.AsMemory(), WebSocketMessageType.Binary, true, timeout.Token);
        var response = new byte[128];
        var result = await socket.ReceiveAsync(response.AsMemory(), timeout.Token);
        Assert.Equal(WebSocketMessageType.Binary, result.MessageType);
        Assert.Equal(payload, response[..result.Count]);
        Assert.Equal("/", Assert.Single(received));
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
    }

    private static Dictionary<string, string?> Settings() => new()
    {
        ["AllowedHosts"] = "api.arkya.online",
        ["Gateway:CharactersEnabled"] = "true",
        ["Gateway:RealmGameEnabled"] = "true",
        ["Gateway:RealmGameAddress"] = RealmAddress,
        ["ReverseProxy:Clusters:identity:Destinations:primary:Address"] = "http://127.0.0.1:1/",
        ["ReverseProxy:Clusters:characters:Destinations:primary:Address"] = "http://127.0.0.1:1/"
    };

    private static void UseTestRealmDestination(IServiceCollection services, string address)
    {
        var original = services.Single(descriptor => descriptor.ServiceType == typeof(IProxyConfigProvider));
        services.Remove(original);
        services.AddSingleton<IProxyConfigProvider>(provider =>
        {
            // Validate production options and routes before replacing only the test socket destination.
            var config = ((IProxyConfigProvider)original.ImplementationFactory!(provider)).GetConfig();
            return new InMemoryConfigProvider(config.Routes, config.Clusters.Select(cluster => cluster.ClusterId != "realm-game"
                ? cluster : cluster with { Destinations = new Dictionary<string, DestinationConfig>
                {
                    ["test"] = new() { Address = address }
                } }).ToArray());
        });
    }

    private sealed class RealmGameFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(Settings()));
        }
    }
}
