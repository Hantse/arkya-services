using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Arkya.Realms.Tests;

public sealed class InternalHttpTests
{
    [Theory]
    [InlineData("10.42.1.9", "203.0.113.9", 200)]
    [InlineData("::ffff:10.42.1.9", "203.0.113.9", 200)]
    [InlineData("192.168.100.21", "10.42.1.9", 400)]
    public async Task HttpAuthorizationUsesThePeerBeforeForwardedHeaders(string peer, string forwarded, int status)
    {
        await using var factory = new RealmFactory(production: true);
        factory.Settings["InternalHttp:AllowedNetworks:0"] = "10.42.0.0/16";
        factory.Settings["ProxyTrust:KnownNetworks:0"] = "10.42.0.0/16";
        factory.Settings["ProxyTrust:KnownNetworks:1"] = "192.168.100.0/24";
        var response = await factory.Server.SendAsync(context =>
        {
            ConfigureRequest(context, peer, "/admin/realms");
            context.Request.Headers["X-Arkya-Admin-Key"] = RealmFactory.AdminKey;
            context.Request.Headers["X-Forwarded-For"] = forwarded;
            context.Request.Headers["X-Forwarded-Proto"] = "http";
        });
        Assert.Equal(status, response.Response.StatusCode);
        Assert.Equal(forwarded, response.Connection.RemoteIpAddress!.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-key")]
    public async Task AllowedHttpStillRequiresAdminAuthentication(string? key)
    {
        await using var factory = new RealmFactory(production: true);
        factory.Settings["InternalHttp:AllowedNetworks:0"] = "10.42.0.0/16";
        var response = await factory.Server.SendAsync(context =>
        {
            ConfigureRequest(context, "10.42.1.9", "/admin/realms");
            if (key is not null) context.Request.Headers["X-Arkya-Admin-Key"] = key;
        });
        Assert.Equal(403, response.Response.StatusCode);
    }

    [Fact]
    public async Task ForwardedHttpsAndPlayerCatalogKeysCannotBypassTheBoundary()
    {
        await using var factory = new RealmFactory(production: true);
        factory.Settings["InternalHttp:AllowedNetworks:0"] = "10.42.0.0/16";
        factory.Settings["ProxyTrust:KnownNetworks:0"] = "10.42.0.0/16";
        factory.Settings["GameAccess:ServiceKey"] = "realms-player-catalog-test-key-123456";
        var external = await factory.Server.SendAsync(context =>
        {
            ConfigureRequest(context, "203.0.113.9", "/admin/realms");
            context.Request.Headers["X-Arkya-Admin-Key"] = RealmFactory.AdminKey;
            context.Request.Headers["X-Forwarded-For"] = "10.42.1.9";
            context.Request.Headers["X-Forwarded-Proto"] = "https";
        });
        Assert.Equal(400, external.Response.StatusCode);
        var missingKey = await factory.Server.SendAsync(context =>
            ConfigureRequest(context, "10.42.1.9", "/internal/realms/entries"));
        Assert.Equal(403, missingKey.Response.StatusCode);
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task HealthProbesRequireAnAllowedPeer(string path)
    {
        await using var factory = new RealmFactory(production: true);
        factory.Settings["InternalHttp:AllowedNetworks:0"] = "192.168.100.0/24";
        var allowed = await factory.Server.SendAsync(context => ConfigureRequest(context, "192.168.100.21", path));
        Assert.Equal(200, allowed.Response.StatusCode);
        var denied = await factory.Server.SendAsync(context => ConfigureRequest(context, "203.0.113.9", path));
        Assert.Equal(400, denied.Response.StatusCode);
    }

    [Fact]
    public void PublicHttpAllowlistFailsStartup()
    {
        using var factory = new RealmFactory(production: true);
        factory.Settings["InternalHttp:AllowedNetworks:0"] = "0.0.0.0/0";
        Assert.Throws<OptionsValidationException>(() => factory.Client());
    }

    private static void ConfigureRequest(HttpContext context, string peer, string path)
    {
        context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
        context.Request.Host = new HostString("localhost");
        context.Request.Method = "GET";
        context.Request.Path = path;
        context.Request.Scheme = "http";
    }
}
