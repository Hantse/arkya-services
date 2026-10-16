using System.Diagnostics;
using System.Net;
using Arkya.Identity.Api.Application;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Arkya.Identity.Tests;

public sealed class RuntimeDeploymentTests
{
    [Theory]
    [InlineData("ReverseProxy:KnownProxies:0", "10.20.0.7", "10.20.0.7", true)]
    [InlineData("ReverseProxy:KnownProxies:0", "10.20.0.7", "10.20.0.8", false)]
    [InlineData("ReverseProxy:KnownNetworks:0", "10.20.0.0/24", "10.20.0.8", true)]
    [InlineData("ReverseProxy:KnownNetworks:0", "10.20.0.0/24", "10.21.0.8", false)]
    [InlineData("ReverseProxy:KnownNetworks:0", "10.20.0.0/24", "::ffff:10.20.0.8", true)]
    public async Task OnlyTrustedProxyCanRestoreHttpsAndClientAddress(string key, string trusted, string remote, bool allowed)
    {
        await using var factory = Production(new() { [key] = trusted });
        var response = await SendAsync(factory, "/health", remote: remote, forwarded: true);

        Assert.Equal(allowed ? 200 : 400, response.Response.StatusCode);
        Assert.Equal(allowed ? "https" : "http", response.Request.Scheme);
        Assert.Equal(allowed ? "203.0.113.10" : remote, response.Connection.RemoteIpAddress!.ToString());
        Assert.Equal("localhost", response.Request.Host.Host);
    }

    [Fact]
    public async Task EmptyTrustDoesNotEvenTrustLoopbackHeaders()
    {
        await using var factory = Production();
        var response = await SendAsync(factory, "/health", remote: "127.0.0.1", forwarded: true);
        Assert.Equal(400, response.Response.StatusCode);
        Assert.Equal("http", response.Request.Scheme);
    }

    [Fact]
    public async Task ForwardingConsumesOnlyTheConfiguredRightmostHop()
    {
        await using var factory = Production(new() { ["ReverseProxy:KnownProxies:0"] = "10.20.0.7" });
        var response = await factory.Server.SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse("10.20.0.7");
            context.Request.Method = "GET";
            context.Request.Path = "/health";
            context.Request.Host = new HostString("localhost");
            context.Request.Headers["X-Forwarded-For"] = "198.51.100.10, 203.0.113.10";
            context.Request.Headers["X-Forwarded-Proto"] = "http, https";
        });
        Assert.Equal(200, response.Response.StatusCode);
        Assert.Equal("203.0.113.10", response.Connection.RemoteIpAddress!.ToString());
        Assert.Equal("https", response.Request.Scheme);
    }

    [Theory]
    [InlineData("ReverseProxy:KnownProxies:0", "0.0.0.0")]
    [InlineData("ReverseProxy:KnownProxies:0", "::")]
    [InlineData("ReverseProxy:KnownProxies:0", "::ffff:0.0.0.0")]
    [InlineData("ReverseProxy:KnownProxies:0", "::ffff:224.0.0.1")]
    [InlineData("ReverseProxy:KnownProxies:0", "ingress.example.com")]
    [InlineData("ReverseProxy:KnownProxies:0", "1")]
    [InlineData("ReverseProxy:KnownProxies:0", "10.0.0.1/24")]
    [InlineData("ReverseProxy:KnownProxies:0", " 10.0.0.1")]
    [InlineData("ReverseProxy:KnownNetworks:0", "0.0.0.0/0")]
    [InlineData("ReverseProxy:KnownNetworks:0", "::/0")]
    [InlineData("ReverseProxy:KnownNetworks:0", "::ffff:0.0.0.0/96")]
    [InlineData("ReverseProxy:KnownNetworks:0", "::/80")]
    [InlineData("ReverseProxy:KnownNetworks:0", "invalid")]
    [InlineData("ReverseProxy:ForwardLimit", "0")]
    [InlineData("ReverseProxy:ForwardLimit", "5")]
    [InlineData("Management:Port", "80")]
    [InlineData("Management:Port", "65536")]
    [InlineData("Management:ReadinessTimeout", "00:00:00")]
    [InlineData("Management:ReadinessTimeout", "00:00:11")]
    [InlineData("DataProtection:KeysPath", "relative/keys")]
    public async Task InvalidRuntimeConfigurationFailsStartup(string key, string value)
    {
        await using var factory = Production(new() { [key] = value });
        Assert.Throws<OptionsValidationException>(() => factory.BrowserClient());
    }

    [Theory]
    [InlineData("/v1/auth/me", "GET")]
    [InlineData("/v1/auth/discord/challenges", "POST")]
    [InlineData("/internal/sessions/introspect", "POST")]
    [InlineData("/health", "GET")]
    [InlineData("/health/live", "POST")]
    [InlineData("/health/ready", "POST")]
    [InlineData("/health/live/extra", "GET")]
    public async Task ManagementPortCannotServeApplicationEndpoints(string path, string method)
    {
        await using var factory = Production(new() { ["Management:Port"] = "8081" });
        var response = await SendAsync(factory, path, port: 8081, method: method);
        Assert.Equal(404, response.Response.StatusCode);
        Assert.Empty(factory.Store.Accounts);
    }

    [Fact]
    public async Task LivenessIsHttpAndIndependentOfMongoWhileReadinessFailsClosed()
    {
        await using var factory = Production(new()
        {
            ["Management:Port"] = "8081",
            ["Management:ReadinessTimeout"] = "00:00:00.100",
            ["Mongo:ConnectionString"] = "mongodb://127.0.0.1:1/?directConnection=true"
        });
        var live = await SendAsync(factory, "/health/live", port: 8081);
        Assert.Equal(200, live.Response.StatusCode);
        Assert.Equal("no-store", live.Response.Headers.CacheControl.ToString());
        var started = Stopwatch.StartNew();
        var ready = await SendAsync(factory, "/health/ready", port: 8081);
        Assert.Equal(503, ready.Response.StatusCode);
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(3));
        using var body = new StreamReader(ready.Response.Body);
        Assert.Equal("{\"status\":\"not_ready\"}", await body.ReadToEndAsync());
    }

    [Fact]
    public async Task HostHeaderCannotMoveApiRequestToManagementPort()
    {
        await using var factory = Production(new() { ["Management:Port"] = "8081" });
        var response = await factory.Server.SendAsync(context =>
        {
            context.Connection.LocalPort = 8080;
            context.Request.Method = "GET";
            context.Request.Path = "/health/live";
            context.Request.Host = new HostString("localhost", 8081);
            context.Request.Scheme = "https";
        });
        Assert.Equal(404, response.Response.StatusCode);
    }

    [Fact]
    public async Task ManagementRemainsDisabledByDefaultAndApiRequiresHttps()
    {
        await using var factory = Production();
        Assert.Equal(400, (await SendAsync(factory, "/health/live", port: 8081)).Response.StatusCode);
        Assert.Equal(400, (await SendAsync(factory, "/v1/auth/me", port: 8080)).Response.StatusCode);
    }

    [Fact]
    public async Task PersistedKeysSurviveAReplacementIdentityProcess()
    {
        var keysPath = Path.Combine(Path.GetTempPath(), "arkya-identity-keys-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            string ticket;
            await using (var first = Production(new() { ["DataProtection:KeysPath"] = keysPath }))
            {
                ticket = first.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("deployment-test").Protect("social-refresh-test");
                Assert.NotEmpty(Directory.GetFiles(keysPath, "key-*.xml"));
            }
            await using var replacement = Production(new() { ["DataProtection:KeysPath"] = keysPath });
            var cleartext = replacement.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("deployment-test").Unprotect(ticket);
            Assert.Equal("social-refresh-test", cleartext);
        }
        finally
        {
            if (Directory.Exists(keysPath)) Directory.Delete(keysPath, recursive: true);
        }
    }

    private static IdentityFactory Production(Dictionary<string, string?>? configuration = null) => new()
    {
        EnvironmentName = "Production", AdditionalConfiguration = configuration ?? []
    };

    private static Task<HttpContext> SendAsync(IdentityFactory factory, string path, int port = 8080,
        string remote = "10.20.0.8", bool forwarded = false, string method = "GET") => factory.Server.SendAsync(context =>
    {
        context.Connection.LocalPort = port;
        context.Connection.RemoteIpAddress = IPAddress.Parse(remote);
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("localhost");
        context.Request.Method = method;
        context.Request.Path = path;
        if (!forwarded) return;
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.10";
        context.Request.Headers["X-Forwarded-Proto"] = "https";
        context.Request.Headers["X-Forwarded-Host"] = "attacker.example";
    });
}
