using System.Net;
using Microsoft.AspNetCore.Http;

namespace Arkya.Characters.Tests;

public sealed class GatewayProxyTests
{
    [Theory]
    [InlineData("10.42.1.9", true)]
    [InlineData("::ffff:10.42.1.9", true)]
    [InlineData("192.168.100.9", false)]
    public async Task ProductionAcceptsForwardedHttpsOnlyFromConfiguredGatewayNetwork(string source, bool accepted)
    {
        await using var factory = new CharactersFactory
        {
            EnvironmentName = "Production",
            AdditionalConfiguration = new Dictionary<string, string?> { ["ProxyTrust:KnownNetworks:0"] = "10.42.0.0/16" }
        };
        var response = await factory.Server.SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(source);
            context.Request.Host = new HostString("api.arkya.online");
            context.Request.Method = "GET";
            context.Request.Path = "/v1/characters/appearance-options";
            context.Request.Headers["X-Forwarded-Proto"] = "https";
            context.Request.Headers["X-Forwarded-For"] = "203.0.113.5";
            context.Request.Headers["X-Forwarded-Host"] = "attacker.invalid";
        });
        Assert.Equal(accepted ? 200 : 400, response.Response.StatusCode);
        Assert.Equal("api.arkya.online", response.Request.Host.Host);
        Assert.Equal(accepted ? "203.0.113.5" : source, response.Connection.RemoteIpAddress!.ToString());
    }

    [Fact]
    public async Task GatewayDoesNotReplaceAccountAuthentication()
    {
        await using var factory = new CharactersFactory
        {
            EnvironmentName = "Production",
            AdditionalConfiguration = new Dictionary<string, string?> { ["ProxyTrust:KnownNetworks:0"] = "10.42.0.0/16" }
        };
        var response = await factory.Server.SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse("10.42.1.9");
            context.Request.Host = new HostString("api.arkya.online");
            context.Request.Method = "GET";
            context.Request.Path = "/v1/characters";
            context.Request.Headers["X-Forwarded-Proto"] = "https";
        });
        Assert.Equal(401, response.Response.StatusCode);
        Assert.Equal(0, factory.Identity.Calls);
    }
}
