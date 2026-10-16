using System.Net;
using Microsoft.Extensions.Options;

namespace Arkya.Gateway.Tests;

public sealed class GatewayAdminTests
{
    private static Dictionary<string, string?> Enabled(string address, string host = "backoffice.arkya.online") => new()
    {
        ["AllowedHosts"] = $"api.arkya.online;{host}",
        ["Gateway:AdminEnabled"] = "true",
        ["Gateway:AdminHost"] = host,
        ["Gateway:AdminAddress"] = address
    };

    [Theory]
    [InlineData("/")]
    [InlineData("/Login")]
    [InlineData("/Realms")]
    [InlineData("/admin.css")]
    [InlineData("/health")]
    [InlineData("/v1/auth/me")]
    [InlineData("/v1/characters")]
    public async Task Admin_host_routes_only_to_admin_including_overlapping_paths(string path)
    {
        await using var identity = await Downstream.StartAsync("identity");
        await using var admin = await Downstream.StartAsync("admin");
        await using var factory = new GatewayFactory(identity.Address, identity.Address, Enabled(admin.Address));
        using var client = factory.Client();
        using var response = await client.GetAsync($"https://backoffice.arkya.online{path}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("admin", await response.Content.ReadAsStringAsync());
        Assert.Empty(identity.Requests);
        var received = Assert.Single(admin.Requests);
        Assert.Equal(path, received.Path);
        Assert.Equal("backoffice.arkya.online", received.Headers["Host"]);
    }

    [Fact]
    public async Task Admin_form_preserves_cookie_body_and_normalizes_trusted_proxy_headers()
    {
        await using var identity = await Downstream.StartAsync("identity");
        await using var admin = await Downstream.StartAsync("admin");
        await using var factory = new GatewayFactory(identity.Address, overrides: Enabled(admin.Address));
        using var client = factory.Client("http");
        using var request = new HttpRequestMessage(HttpMethod.Post, "http://backoffice.arkya.online/Realms?handler=Deploy");
        request.Headers.Add("Cookie", "Arkya.Admin=private-cookie");
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Forwarded-For", "203.0.113.7");
        request.Headers.Add("X-Forwarded-Host", "attacker.invalid");
        request.Headers.Add("X-Forwarded-Prefix", "/internal");
        request.Headers.Add("X-Arkya-Service-Key", "untrusted-client-key");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = "antiforgery-token",
            ["RealmId"] = "realm-one"
        });
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(identity.Requests);
        var received = Assert.Single(admin.Requests);
        Assert.Equal("POST", received.Method);
        Assert.Equal("?handler=Deploy", received.Query);
        Assert.Contains("RealmId=realm-one", received.Body);
        Assert.Contains("__RequestVerificationToken=antiforgery-token", received.Body);
        Assert.Equal("Arkya.Admin=private-cookie", received.Headers["Cookie"]);
        Assert.Equal("backoffice.arkya.online", received.Headers["Host"]);
        Assert.Equal("https", received.Headers["X-Forwarded-Proto"]);
        Assert.Equal("203.0.113.7", received.Headers["X-Forwarded-For"]);
        Assert.DoesNotContain("X-Forwarded-Host", received.Headers.Keys);
        Assert.DoesNotContain("X-Forwarded-Prefix", received.Headers.Keys);
        Assert.DoesNotContain("X-Arkya-Service-Key", received.Headers.Keys);
        Assert.DoesNotContain(factory.Logs.Messages, message => message.Contains("private-cookie") || message.Contains("antiforgery-token"));
    }

    [Theory]
    [InlineData("/Login")]
    [InlineData("/Realms")]
    [InlineData("/admin/realms")]
    [InlineData("/internal/sessions/introspect")]
    public async Task Api_host_never_uses_admin_catchall_even_with_forged_forwarded_host(string path)
    {
        await using var identity = await Downstream.StartAsync("identity");
        await using var admin = await Downstream.StartAsync("admin");
        await using var factory = new GatewayFactory(identity.Address, overrides: Enabled(admin.Address));
        using var client = factory.Client();
        client.DefaultRequestHeaders.Add("X-Forwarded-Host", "backoffice.arkya.online");
        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(identity.Requests);
        Assert.Empty(admin.Requests);
    }

    [Fact]
    public async Task Enabling_admin_preserves_api_routes_and_local_gateway_health()
    {
        await using var identity = await Downstream.StartAsync("identity");
        await using var admin = await Downstream.StartAsync("admin");
        await using var factory = new GatewayFactory(identity.Address, overrides: Enabled(admin.Address));
        using var client = factory.Client();
        using var api = await client.GetAsync("/v1/auth/me");
        using var health = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, api.StatusCode);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Contains("arkya-gateway", await health.Content.ReadAsStringAsync());
        Assert.Single(identity.Requests);
        Assert.Empty(admin.Requests);
    }

    [Fact]
    public async Task Admin_is_disabled_by_default_even_when_destination_is_set()
    {
        await using var admin = await Downstream.StartAsync("admin");
        var settings = Enabled(admin.Address);
        settings.Remove("Gateway:AdminEnabled");
        await using var factory = new GatewayFactory(admin.Address, overrides: settings);
        using var client = factory.Client();
        using var response = await client.GetAsync("https://backoffice.arkya.online/Login");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(admin.Requests);
    }

    [Fact]
    public async Task Custom_admin_host_is_exact_and_unknown_hosts_are_rejected()
    {
        await using var admin = await Downstream.StartAsync("admin");
        await using var factory = new GatewayFactory(admin.Address, overrides: Enabled(admin.Address, "operations.arkya.online"));
        using var client = factory.Client();
        using var valid = await client.GetAsync("https://operations.arkya.online/Login");
        using var unknown = await client.GetAsync("https://backoffice.arkya.online/Login");

        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Single(admin.Requests);
    }

    [Fact]
    public async Task Admin_host_cannot_expose_routes_on_management_listener()
    {
        await using var admin = await Downstream.StartAsync("admin");
        await using var factory = new GatewayFactory(admin.Address, overrides: Enabled(admin.Address), localPort: 8081);
        using var client = factory.Client("http");
        using var response = await client.GetAsync("http://backoffice.arkya.online/Realms");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(admin.Requests);
    }

    [Theory]
    [InlineData("Gateway:AdminHost", "api.arkya.online")]
    [InlineData("Gateway:AdminHost", "API.ARKYA.ONLINE")]
    [InlineData("Gateway:AdminHost", "*.arkya.online")]
    [InlineData("Gateway:AdminHost", "https://backoffice.arkya.online")]
    [InlineData("Gateway:AdminHost", "backoffice.arkya.online:443")]
    [InlineData("Gateway:AdminHost", "backoffice.arkya.online.")]
    [InlineData("Gateway:AdminHost", "backoffice..arkya.online")]
    [InlineData("Gateway:AdminHost", "-backoffice.arkya.online")]
    [InlineData("Gateway:AdminHost", "127.0.0.1")]
    [InlineData("Gateway:AdminHost", "localhost")]
    [InlineData("Gateway:AdminHost", "")]
    [InlineData("Gateway:AdminAddress", "")]
    [InlineData("Gateway:AdminAddress", "ftp://admin.internal/")]
    [InlineData("Gateway:AdminAddress", "http://user:password@admin.internal/")]
    [InlineData("Gateway:AdminAddress", "http://admin.internal/prefix/")]
    [InlineData("Gateway:AdminAddress", "http://admin.internal/?query=1")]
    [InlineData("Gateway:AdminAddress", "http://admin.internal/#fragment")]
    [InlineData("Gateway:AdminAddress", " http://admin.internal/")]
    [InlineData("Gateway:AdminAddress", "http://admin.internal\\")]
    [InlineData("AllowedHosts", "*")]
    [InlineData("AllowedHosts", "api.arkya.online;*.arkya.online;backoffice.arkya.online")]
    [InlineData("AllowedHosts", "api.arkya.online")]
    public void Invalid_admin_configuration_fails_at_startup(string key, string value)
    {
        var settings = Enabled("http://admin.internal:8080/");
        settings[key] = value;
        using var factory = new GatewayFactory("http://identity.internal/", overrides: settings);
        Assert.Throws<OptionsValidationException>(() => factory.Client());
    }
}
