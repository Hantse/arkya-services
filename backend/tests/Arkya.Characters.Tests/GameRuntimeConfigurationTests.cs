using System.Net;
using System.Text;
using Arkya.Characters.Api.Application;
using Arkya.ServiceDefaults;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arkya.Characters.Tests;

public sealed class GameRuntimeConfigurationTests
{
    private const string DedicatedKey = "dedicated-runtime-test-key-thirty-two-characters";

    [Theory]
    [InlineData("https://realms.example", false, false, true)]
    [InlineData("http://localhost:5088", false, false, false)]
    [InlineData("http://localhost:5088", true, false, true)]
    [InlineData("http://arkya-realms.arkya-beta.svc.cluster.local:8080/", false, true, true)]
    [InlineData("http://arkya-realms.arkya-beta.svc.cluster.local:8080/", false, false, false)]
    [InlineData("http://realms.example/", false, true, false)]
    [InlineData("http://192.168.100.1/", false, true, false)]
    [InlineData("http://localhost:8080/", false, true, false)]
    [InlineData("http://arkya-realms.arkya-beta.svc.cluster.local.evil/", false, true, false)]
    [InlineData("http://user:password@arkya-realms.arkya-beta.svc.cluster.local/", false, true, false)]
    [InlineData("http://arkya-realms.arkya-beta.svc.cluster.local/path", false, true, false)]
    [InlineData("http://arkya-realms.arkya-beta.svc.cluster.local/?query=1", false, true, false)]
    [InlineData("http://arkya-realms.arkya-beta.svc.cluster.local/#fragment", false, true, false)]
    public void RealmCatalogTransportRequiresHttpsOrAnExplicitExactClusterService(string url, bool development, bool optIn, bool expected)
    {
        Assert.Equal(expected, new RealmClientOptions
        {
            BaseUrl = url, ServiceKey = "realm-catalog-test-key-thirty-two-characters", AllowClusterHttp = optIn
        }.IsValid(development));
    }

    [Theory]
    [InlineData("10.42.1.9", false, true, 400)]
    [InlineData("10.42.1.9", true, false, 403)]
    [InlineData("10.42.1.9", true, true, 409)]
    [InlineData("::ffff:10.42.1.9", true, true, 409)]
    [InlineData("203.0.113.9", true, true, 400)]
    public async Task PrivateDedicatedTransportRequiresOptInDirectPeerAndBoundKey(string peer, bool optIn, bool validKey, int expected)
    {
        await using var factory = Factory(optIn);
        var response = await factory.Server.SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
            context.Request.Host = new HostString("localhost");
            context.Request.Method = "POST";
            context.Request.Path = "/internal/game/admissions/consume";
            context.Request.Scheme = "http";
            context.Request.Headers["X-Arkya-Dedicated-Key"] = validKey ? DedicatedKey : "wrong";
            context.Request.Headers["X-Forwarded-Proto"] = "https";
            context.Request.Headers["X-Forwarded-For"] = "10.42.1.8";
            var body = Encoding.UTF8.GetBytes("{\"ticket\":\"invalid\"}");
            context.Request.ContentType = "application/json";
            context.Request.ContentLength = body.Length;
            context.Request.Body = new MemoryStream(body);
            context.Features.Set<IHttpRequestBodyDetectionFeature>(new RequestBodyDetection());
        });
        Assert.Equal(expected, response.Response.StatusCode);
        Assert.Equal(optIn && peer != "203.0.113.9", response.IsArkyaInternalHttpAllowed());
    }

    [Theory]
    [InlineData("/internal/gameplay/admissions/consume")]
    [InlineData("/internal/characters/00000000-0000-0000-0000-000000000001")]
    [InlineData("/v1/characters/appearance-options")]
    public async Task DedicatedOptInDoesNotRelaxOtherPrivateOrPublicRoutes(string path)
    {
        await using var factory = Factory(true);
        var response = await factory.Server.SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse("10.42.1.9");
            context.Request.Host = new HostString("localhost");
            context.Request.Method = "GET";
            context.Request.Path = path;
            context.Request.Scheme = "http";
        });
        Assert.False(response.IsArkyaInternalHttpAllowed());
        Assert.Equal(400, response.Response.StatusCode);
    }

    private static CharactersFactory Factory(bool allowPrivateHttp) => new()
    {
        EnvironmentName = "Production",
        AdditionalConfiguration = new Dictionary<string, string?>
        {
            ["GameAccess:AllowPrivateHttp"] = allowPrivateHttp.ToString(),
            ["GameAccess:DedicatedServers:0:RealmId"] = "beta-web",
            ["GameAccess:DedicatedServers:0:ServerId"] = "island",
            ["GameAccess:DedicatedServers:0:Key"] = DedicatedKey,
            ["InternalHttp:AllowedNetworks:0"] = "10.42.0.0/16",
            ["ProxyTrust:KnownNetworks:0"] = "10.42.0.0/16"
        },
        ConfigureTestServices = services =>
        {
            services.RemoveAll<IGameLeaseStore>();
            services.AddSingleton<IGameLeaseStore, MemoryGameLeaseStore>();
        }
    };

    private sealed class RequestBodyDetection : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }
}
