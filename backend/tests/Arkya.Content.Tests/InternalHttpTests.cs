using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Arkya.Content.Tests;

public sealed class InternalHttpTests
{
    [Theory]
    [InlineData("10.42.1.9", true)]
    [InlineData("::ffff:10.42.1.9", true)]
    [InlineData("192.168.100.21", true)]
    [InlineData("fd12::9", true)]
    [InlineData("10.43.1.9", false)]
    [InlineData("203.0.113.9", false)]
    public async Task ProductionHttpRequiresAnAllowlistedTransportPeer(string peer, bool accepted)
    {
        await using var factory = ConfiguredFactory();
        var response = await factory.Server.SendAsync(context =>
        {
            ConfigureRequest(context, peer, "/admin/content/draft");
            context.Request.Headers["X-Arkya-Admin-Key"] = ContentFactory.AdminKey;
            context.Request.Headers["X-Forwarded-For"] = "10.42.1.9";
            context.Request.Headers["X-Forwarded-Proto"] = "https";
        });
        Assert.Equal(accepted ? 200 : 400, response.Response.StatusCode);
    }

    [Theory]
    [InlineData("/admin/content/draft", "X-Arkya-Admin-Key")]
    [InlineData("/internal/content/catalogs/95f1b50c-85ba-49a6-9b75-e12745472cf4", "X-Arkya-Service-Key")]
    public async Task AllowedHttpStillRequiresTheCorrectServiceCredential(string path, string header)
    {
        await using var factory = ConfiguredFactory();
        foreach (var key in new[] { "", "wrong-key" })
        {
            var response = await factory.Server.SendAsync(context =>
            {
                ConfigureRequest(context, "10.42.1.9", path);
                if (key.Length > 0) context.Request.Headers[header] = key;
            });
            Assert.Equal(403, response.Response.StatusCode);
        }
    }

    [Fact]
    public async Task HealthUsesTheSameNetworkBoundaryAndHttpRemainsDisabledByDefault()
    {
        await using var configured = ConfiguredFactory();
        var allowed = await configured.Server.SendAsync(context => ConfigureRequest(context, "192.168.100.21", "/health"));
        Assert.Equal(200, allowed.Response.StatusCode);
        await using var defaults = new ContentFactory(production: true);
        var denied = await defaults.Server.SendAsync(context => ConfigureRequest(context, "10.42.1.9", "/health"));
        Assert.Equal(400, denied.Response.StatusCode);
        using var secure = defaults.Client();
        Assert.Equal(HttpStatusCode.OK, (await secure.GetAsync("/health")).StatusCode);
    }

    [Theory]
    [InlineData("0.0.0.0/0")]
    [InlineData("::/0")]
    [InlineData("10.0.0.0/7")]
    [InlineData("172.0.0.0/8")]
    [InlineData("192.168.0.0/15")]
    [InlineData("203.0.113.0/24")]
    [InlineData("127.0.0.0/8")]
    [InlineData("169.254.0.0/16")]
    [InlineData("fe80::/10")]
    [InlineData("::ffff:10.42.0.0/112")]
    [InlineData("10.42.0.0/16 ")]
    [InlineData("")]
    [InlineData("not-a-network")]
    public void NonPrivateOrMalformedAllowlistFailsStartup(string network)
    {
        using var factory = new ContentFactory(production: true);
        factory.Settings["InternalHttp:AllowedNetworks:0"] = network;
        Assert.Throws<OptionsValidationException>(() => factory.Client());
    }

    private static ContentFactory ConfiguredFactory()
    {
        var factory = new ContentFactory(production: true);
        factory.Settings["InternalHttp:AllowedNetworks:0"] = "10.42.0.0/16";
        factory.Settings["InternalHttp:AllowedNetworks:1"] = "192.168.100.0/24";
        factory.Settings["InternalHttp:AllowedNetworks:2"] = "fd12::/64";
        return factory;
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
