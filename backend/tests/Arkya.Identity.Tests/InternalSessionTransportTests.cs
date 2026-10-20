using System.Net;
using System.Text;
using Arkya.Identity.Api.Application;
using Arkya.Identity.Api.Domain;
using Arkya.ServiceDefaults;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Arkya.Identity.Tests;

public sealed class InternalSessionTransportTests
{
    [Theory]
    [InlineData("10.42.1.9", true)]
    [InlineData("::ffff:10.42.1.9", true)]
    [InlineData("192.168.100.21", true)]
    [InlineData("203.0.113.9", false)]
    public async Task PrivateHttpIntrospectionRequiresAnAllowlistedDirectPeerAndStillAuthenticatesService(string peer, bool allowed)
    {
        await using var factory = Configured();
        var syntheticSession = new string('a', 43);
        var account = await factory.Store.UpsertAccountAsync("123", "Test player", factory.Clock.GetUtcNow().UtcDateTime, default);
        await factory.Store.CreateSessionAsync(new Session(IdentityService.Hash(syntheticSession),
            account.Id, "Test player", factory.Clock.GetUtcNow().AddHours(1).UtcDateTime), default);
        foreach (var validKey in new[] { false, true })
        {
            var response = await factory.Server.SendAsync(context =>
            {
                Request(context, peer, "/internal/sessions/introspect", "POST");
                context.Request.Headers["X-Arkya-Service-Key"] = validKey ? IdentityFactory.ServiceKey : "wrong";
                context.Request.Headers["X-Forwarded-For"] = "10.42.1.9";
                context.Request.Headers["X-Forwarded-Proto"] = "https";
                context.Request.ContentType = "application/json";
                context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes($"{{\"sessionToken\":\"{syntheticSession}\"}}"));
                context.Request.ContentLength = context.Request.Body.Length;
                context.Features.Set<IHttpRequestBodyDetectionFeature>(new RequestBodyDetection());
            });
            Assert.Equal(allowed, response.IsArkyaInternalHttpAllowed());
            var responseText = await new StreamReader(response.Response.Body).ReadToEndAsync();
            Assert.True(response.Response.StatusCode == (!allowed ? 400 : validKey ? 200 : 403),
                $"Status {response.Response.StatusCode}: {responseText}");
        }
    }

    [Theory]
    [InlineData("/v1/auth/me")]
    [InlineData("/v1/auth/discord/challenges")]
    [InlineData("/internal/other")]
    [InlineData("/health")]
    public async Task AllowlistDoesNotEnablePlainHttpPublicIdentityRoutes(string path)
    {
        await using var factory = Configured();
        var response = await factory.Server.SendAsync(context => Request(context, "10.42.1.9", path, "GET"));
        Assert.Equal(400, response.Response.StatusCode);
    }

    [Fact]
    public async Task PrivateHttpIsDisabledByDefault()
    {
        await using var factory = new IdentityFactory { EnvironmentName = "Production" };
        var response = await factory.Server.SendAsync(context =>
            Request(context, "10.42.1.9", "/internal/sessions/introspect", "POST"));
        Assert.Equal(400, response.Response.StatusCode);
    }

    private static IdentityFactory Configured() => new()
    {
        EnvironmentName = "Production", AdditionalConfiguration = new Dictionary<string, string?>
        {
            ["InternalHttp:AllowedNetworks:0"] = "10.42.0.0/16",
            ["InternalHttp:AllowedNetworks:1"] = "192.168.100.0/24"
        }
    };

    private static void Request(HttpContext context, string peer, string path, string method)
    {
        context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
        context.Request.Host = new HostString("localhost");
        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.Scheme = "http";
    }

    private sealed class RequestBodyDetection : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }
}
