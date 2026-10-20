using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Arkya.Gateway.Tests;

public sealed class GatewayFrontendTests
{
    private static Dictionary<string, string?> Enabled(string address, string host = "arkya.online") => new()
    {
        ["AllowedHosts"] = $"api.arkya.online;{host}",
        ["Gateway:FrontendEnabled"] = "true",
        ["Gateway:FrontendHost"] = host,
        ["Gateway:FrontendAddress"] = address
    };

    [Theory]
    [InlineData("/")]
    [InlineData("/index.html")]
    [InlineData("/assets/site.js")]
    [InlineData("/health")]
    [InlineData("/v1/auth/me")]
    [InlineData("/v1/characters")]
    [InlineData("/internal/sessions/introspect")]
    public async Task Frontend_host_routes_only_to_frontend_including_overlapping_paths(string path)
    {
        await using var identity = await Downstream.StartAsync("identity");
        await using var frontend = await Downstream.StartAsync("frontend");
        await using var factory = new GatewayFactory(identity.Address, identity.Address, Enabled(frontend.Address));
        using var client = factory.Client();
        using var response = await client.GetAsync($"https://arkya.online{path}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("frontend", await response.Content.ReadAsStringAsync());
        Assert.Empty(identity.Requests);
        var received = Assert.Single(frontend.Requests);
        Assert.Equal(path, received.Path);
        Assert.Equal("arkya.online", received.Headers["Host"]);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/assets/site.js")]
    [InlineData("/internal/sessions/introspect")]
    [InlineData("/dev/auth/login")]
    [InlineData("/v1/auth/new-route")]
    [InlineData("/v1/auth/discord/social/config")]
    [InlineData("/v1/characters")]
    public async Task Api_host_never_falls_back_to_frontend_even_with_forged_forwarded_host(string path)
    {
        await using var identity = await Downstream.StartAsync("identity");
        await using var frontend = await Downstream.StartAsync("frontend");
        await using var factory = new GatewayFactory(identity.Address, overrides: Enabled(frontend.Address));
        using var client = factory.Client();
        client.DefaultRequestHeaders.Add("X-Forwarded-Host", "arkya.online");
        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(identity.Requests);
        Assert.Empty(frontend.Requests);
    }

    [Fact]
    public async Task Frontend_api_admin_and_gateway_health_keep_separate_destinations()
    {
        await using var identity = await Downstream.StartAsync("identity");
        await using var frontend = await Downstream.StartAsync("frontend");
        await using var admin = await Downstream.StartAsync("admin");
        var settings = Enabled(frontend.Address);
        settings["AllowedHosts"] += ";backoffice.arkya.online";
        settings["Gateway:AdminEnabled"] = "true";
        settings["Gateway:AdminAddress"] = admin.Address;
        await using var factory = new GatewayFactory(identity.Address, overrides: settings);
        using var client = factory.Client();
        using var api = await client.GetAsync("/v1/auth/me");
        using var site = await client.GetAsync("https://arkya.online/v1/auth/me");
        using var backoffice = await client.GetAsync("https://backoffice.arkya.online/v1/auth/me");
        using var health = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, api.StatusCode);
        Assert.Contains("identity", await api.Content.ReadAsStringAsync());
        Assert.Contains("frontend", await site.Content.ReadAsStringAsync());
        Assert.Contains("admin", await backoffice.Content.ReadAsStringAsync());
        Assert.Contains("arkya-gateway", await health.Content.ReadAsStringAsync());
        Assert.Single(identity.Requests);
        Assert.Single(frontend.Requests);
        Assert.Single(admin.Requests);
    }

    [Fact]
    public async Task Frontend_assets_preserve_range_body_cache_and_normalize_trusted_proxy_headers()
    {
        byte[] asset = [0, 1, 127, 128, 255];
        const string cacheControl = "public, max-age=31536000, immutable";
        await using var identity = await Downstream.StartAsync("identity");
        await using var frontend = await Downstream.StartAsync("frontend", async context =>
        {
            context.Response.StatusCode = StatusCodes.Status206PartialContent;
            context.Response.ContentType = "application/wasm";
            context.Response.Headers.CacheControl = cacheControl;
            context.Response.Headers.ETag = "\"web-build\"";
            context.Response.Headers.ContentRange = "bytes 0-4/100";
            await context.Response.Body.WriteAsync(asset, context.RequestAborted);
        });
        await using var factory = new GatewayFactory(identity.Address, overrides: Enabled(frontend.Address));
        using var client = factory.Client("http");
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://arkya.online/build.wasm?v=42");
        request.Headers.Add("Range", "bytes=0-4");
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Forwarded-For", "203.0.113.7");
        request.Headers.Add("X-Forwarded-Host", "api.arkya.online");
        request.Headers.Add("X-Arkya-Service-Key", "untrusted-client-key");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal(asset, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal("application/wasm", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(cacheControl, Assert.Single(response.Headers.GetValues("Cache-Control")));
        Assert.Equal("\"web-build\"", response.Headers.ETag!.Tag);
        Assert.Equal("bytes 0-4/100", response.Content.Headers.ContentRange!.ToString());
        Assert.Empty(identity.Requests);
        var received = Assert.Single(frontend.Requests);
        Assert.Equal("/build.wasm", received.Path);
        Assert.Equal("?v=42", received.Query);
        Assert.Equal("bytes=0-4", received.Headers["Range"]);
        Assert.Equal("arkya.online", received.Headers["Host"]);
        Assert.Equal("https", received.Headers["X-Forwarded-Proto"]);
        Assert.Equal("203.0.113.7", received.Headers["X-Forwarded-For"]);
        Assert.DoesNotContain("X-Forwarded-Host", received.Headers.Keys);
        Assert.DoesNotContain("X-Arkya-Service-Key", received.Headers.Keys);
    }

    [Fact]
    public async Task Discord_callback_preserves_redirect_and_secure_cookie_with_frontend_enabled()
    {
        const string location = "https://arkya.online/?discord=complete";
        const string cookie = "arkya-correlation=; Path=/v1/auth/discord/callback; Secure; HttpOnly; SameSite=Lax";
        await using var identity = await Downstream.StartAsync("identity", context =>
        {
            context.Response.StatusCode = StatusCodes.Status302Found;
            context.Response.Headers.Location = location;
            context.Response.Headers.SetCookie = cookie;
            return Task.CompletedTask;
        });
        await using var frontend = await Downstream.StartAsync("frontend");
        await using var factory = new GatewayFactory(identity.Address, overrides: Enabled(frontend.Address));
        using var client = factory.Client();
        using var response = await client.GetAsync("/v1/auth/discord/callback?code=opaque-code&state=opaque-state");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(location, response.Headers.Location!.OriginalString);
        Assert.Equal(cookie, Assert.Single(response.Headers.GetValues("Set-Cookie")));
        var received = Assert.Single(identity.Requests);
        Assert.Equal("api.arkya.online", received.Headers["Host"]);
        Assert.Equal("?code=opaque-code&state=opaque-state", received.Query);
        Assert.Empty(frontend.Requests);
    }

    [Fact]
    public async Task Frontend_is_disabled_by_default_even_when_destination_is_set()
    {
        await using var frontend = await Downstream.StartAsync("frontend");
        var settings = Enabled(frontend.Address);
        settings.Remove("Gateway:FrontendEnabled");
        await using var factory = new GatewayFactory(frontend.Address, overrides: settings);
        using var client = factory.Client();
        using var response = await client.GetAsync("https://arkya.online/");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(frontend.Requests);
    }

    [Fact]
    public async Task Custom_frontend_host_is_exact_and_unknown_hosts_are_rejected()
    {
        await using var frontend = await Downstream.StartAsync("frontend");
        await using var factory = new GatewayFactory(frontend.Address, overrides: Enabled(frontend.Address, "beta.arkya.online"));
        using var client = factory.Client();
        using var valid = await client.GetAsync("https://beta.arkya.online/");
        using var unknown = await client.GetAsync("https://arkya.online/");

        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Single(frontend.Requests);
    }

    [Fact]
    public async Task Frontend_host_cannot_expose_routes_on_management_listener()
    {
        await using var frontend = await Downstream.StartAsync("frontend");
        await using var factory = new GatewayFactory(frontend.Address, overrides: Enabled(frontend.Address), localPort: 8081);
        using var client = factory.Client("http");
        using var response = await client.GetAsync("http://arkya.online/");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(frontend.Requests);
    }

    [Theory]
    [InlineData("Gateway:FrontendHost", "api.arkya.online")]
    [InlineData("Gateway:FrontendHost", "API.ARKYA.ONLINE")]
    [InlineData("Gateway:FrontendHost", "*.arkya.online")]
    [InlineData("Gateway:FrontendHost", "https://arkya.online")]
    [InlineData("Gateway:FrontendHost", "arkya.online:443")]
    [InlineData("Gateway:FrontendHost", "arkya.online.")]
    [InlineData("Gateway:FrontendHost", "arkya..online")]
    [InlineData("Gateway:FrontendHost", "-arkya.online")]
    [InlineData("Gateway:FrontendHost", "127.0.0.1")]
    [InlineData("Gateway:FrontendHost", "localhost")]
    [InlineData("Gateway:FrontendHost", "")]
    [InlineData("Gateway:FrontendAddress", "")]
    [InlineData("Gateway:FrontendAddress", "ftp://frontend.internal/")]
    [InlineData("Gateway:FrontendAddress", "http://user:password@frontend.internal/")]
    [InlineData("Gateway:FrontendAddress", "http://frontend.internal/prefix/")]
    [InlineData("Gateway:FrontendAddress", "http://frontend.internal/?query=1")]
    [InlineData("Gateway:FrontendAddress", "http://frontend.internal/#fragment")]
    [InlineData("Gateway:FrontendAddress", " http://frontend.internal/")]
    [InlineData("Gateway:FrontendAddress", "http://frontend.internal\\")]
    [InlineData("AllowedHosts", "*")]
    [InlineData("AllowedHosts", "api.arkya.online;*.arkya.online;arkya.online")]
    [InlineData("AllowedHosts", "api.arkya.online")]
    [InlineData("AllowedHosts", "arkya.online")]
    public void Invalid_frontend_configuration_fails_at_startup(string key, string value)
    {
        var settings = Enabled("http://frontend.internal:8080/");
        settings[key] = value;
        using var factory = new GatewayFactory("http://identity.internal/", overrides: settings);
        Assert.Throws<OptionsValidationException>(() => factory.Client());
    }

    [Theory]
    [InlineData("arkya.online")]
    [InlineData("ARKYA.ONLINE")]
    public void Enabled_frontend_and_admin_must_have_distinct_hosts(string adminHost)
    {
        var settings = Enabled("http://frontend.internal:8080/");
        settings["Gateway:AdminEnabled"] = "true";
        settings["Gateway:AdminHost"] = adminHost;
        settings["Gateway:AdminAddress"] = "http://admin.internal:8080/";
        using var factory = new GatewayFactory("http://identity.internal/", overrides: settings);
        Assert.Throws<OptionsValidationException>(() => factory.Client());
    }
}
