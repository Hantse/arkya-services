using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Options;

namespace Arkya.Gateway.Tests;

public sealed class GatewayRealmSitesTests
{
    private static Dictionary<string, string?> Enabled(string frontend, string admin, string realms) => new()
    {
        ["AllowedHosts"] = "api.arkya.online;arkya.online;admin.arkya.online;*.arkya.online;*.admin.arkya.online",
        ["Gateway:FrontendEnabled"] = "true",
        ["Gateway:FrontendHost"] = "arkya.online",
        ["Gateway:FrontendAddress"] = frontend,
        ["Gateway:AdminEnabled"] = "true",
        ["Gateway:AdminHost"] = "admin.arkya.online",
        ["Gateway:AdminAddress"] = admin,
        ["Gateway:RealmSitesEnabled"] = "true",
        ["Gateway:RealmBaseDomain"] = "arkya.online",
        ["Gateway:PublicRealmsEnabled"] = "true",
        ["Gateway:PublicRealmsAddress"] = realms
    };

    [Theory]
    [InlineData("https://beta-one.arkya.online/", "frontend")]
    [InlineData("https://BETA-ONE.arkya.online/assets/site.js", "frontend")]
    [InlineData("https://beta-one.admin.arkya.online/", "admin")]
    [InlineData("https://beta-one.admin.arkya.online/Realms/beta-one", "admin")]
    [InlineData("https://beta-one.admin.arkya.online/v1/public/realms", "admin")]
    [InlineData("https://admin.arkya.online/v1/public/realms", "admin")]
    [InlineData("https://arkya.online/", "frontend")]
    public async Task Realm_and_canonical_hosts_select_their_own_application(string url, string service)
    {
        await using var frontend = await Downstream.StartAsync("frontend");
        await using var admin = await Downstream.StartAsync("admin");
        await using var realms = await Downstream.StartAsync("realms");
        await using var factory = new GatewayFactory(realms.Address,
            overrides: Enabled(frontend.Address, admin.Address, realms.Address));
        using var client = factory.Client();
        using var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(service, await response.Content.ReadAsStringAsync());
        Assert.Empty(realms.Requests);
        var received = Assert.Single(service == "admin" ? admin.Requests : frontend.Requests);
        Assert.Equal(new Uri(url).Host, received.Headers["Host"], ignoreCase: true);
    }

    [Theory]
    [InlineData("https://api.arkya.online/v1/public/realms")]
    [InlineData("https://arkya.online/v1/public/realms")]
    [InlineData("https://beta-one.arkya.online/v1/public/realms")]
    [InlineData("https://beta-one.arkya.online/v1/public/realms/beta-one")]
    [InlineData("https://arkya.online/v1/public/realms/beta-two")]
    public async Task Anonymous_public_catalogue_uses_private_realms_without_player_credentials(string url)
    {
        await using var frontend = await Downstream.StartAsync("frontend");
        await using var admin = await Downstream.StartAsync("admin");
        await using var realms = await Downstream.StartAsync("realms");
        await using var factory = new GatewayFactory(admin.Address,
            overrides: Enabled(frontend.Address, admin.Address, realms.Address));
        using var client = factory.Client();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "private-session");
        client.DefaultRequestHeaders.Add("Cookie", "Arkya.Admin=private-cookie");
        client.DefaultRequestHeaders.Add("X-Arkya-Admin-Key", "must-not-reach-realms");
        using var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Empty(frontend.Requests);
        Assert.Empty(admin.Requests);
        var received = Assert.Single(realms.Requests);
        Assert.Equal(new Uri(url).AbsolutePath, received.Path);
        Assert.Equal(new Uri(realms.Address).Authority, received.Headers["Host"]);
        Assert.DoesNotContain("Authorization", received.Headers.Keys);
        Assert.DoesNotContain("Cookie", received.Headers.Keys);
        Assert.DoesNotContain("X-Arkya-Admin-Key", received.Headers.Keys);
    }

    [Theory]
    [InlineData("https://api.arkya.online/internal/realms")]
    [InlineData("https://api.arkya.online/admin/realms")]
    [InlineData("https://api.arkya.online/Login")]
    [InlineData("https://beta-one.arkya.online/v1/public/realms/beta-two")]
    public async Task Reserved_API_hosts_and_other_realm_details_never_fall_through_wildcard_routes(string url)
    {
        await using var downstream = await Downstream.StartAsync("unexpected");
        await using var factory = new GatewayFactory(downstream.Address,
            overrides: Enabled(downstream.Address, downstream.Address, downstream.Address));
        using var client = factory.Client();
        using var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(downstream.Requests);
    }

    [Theory]
    [InlineData("www.arkya.online")]
    [InlineData("backoffice.arkya.online")]
    [InlineData("beta-game.arkya.online")]
    [InlineData("www.admin.arkya.online")]
    [InlineData("a.arkya.online")]
    [InlineData("1realm.arkya.online")]
    [InlineData("realm-.arkya.online")]
    [InlineData("foo.bar.arkya.online")]
    [InlineData("foo.bar.admin.arkya.online")]
    [InlineData("realm.arkya.online.evil.example")]
    public async Task Invalid_or_reserved_realm_hosts_are_rejected(string host)
    {
        await using var downstream = await Downstream.StartAsync("unexpected");
        await using var factory = new GatewayFactory(downstream.Address,
            overrides: Enabled(downstream.Address, downstream.Address, downstream.Address));
        using var client = factory.Client();
        using var response = await client.GetAsync($"https://{host}/");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(downstream.Requests);
    }

    [Fact]
    public async Task Public_realm_catalogue_does_not_forward_mutations_or_internal_paths()
    {
        await using var downstream = await Downstream.StartAsync("unexpected");
        await using var factory = new GatewayFactory(downstream.Address,
            overrides: Enabled(downstream.Address, downstream.Address, downstream.Address));
        using var client = factory.Client();
        using var mutation = await client.PostAsync("/v1/public/realms", null);
        using var internalRoute = await client.GetAsync("/v1/public/realms/beta-one/internal");

        Assert.Equal(HttpStatusCode.NotFound, mutation.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, internalRoute.StatusCode);
        Assert.Empty(downstream.Requests);
    }

    [Fact]
    public async Task Main_site_catalogue_works_before_optional_realm_domains_are_enabled()
    {
        await using var downstream = await Downstream.StartAsync("realms");
        var settings = Enabled(downstream.Address, downstream.Address, downstream.Address);
        settings["AllowedHosts"] = "api.arkya.online;arkya.online;admin.arkya.online";
        settings["Gateway:RealmSitesEnabled"] = "false";
        await using var factory = new GatewayFactory(downstream.Address, overrides: settings);
        using var client = factory.Client();
        using var response = await client.GetAsync("https://arkya.online/v1/public/realms");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/v1/public/realms", Assert.Single(downstream.Requests).Path);
    }

    [Fact]
    public async Task Anonymous_catalogue_forwards_CORS_preflight_without_credentials()
    {
        await using var downstream = await Downstream.StartAsync("realms");
        await using var factory = new GatewayFactory(downstream.Address,
            overrides: Enabled(downstream.Address, downstream.Address, downstream.Address));
        using var client = factory.Client();
        using var request = new HttpRequestMessage(HttpMethod.Options, "https://beta-one.arkya.online/v1/public/realms/beta-one");
        request.Headers.Add("Origin", "https://beta-one.arkya.online");
        request.Headers.Add("Access-Control-Request-Method", "GET");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var received = Assert.Single(downstream.Requests);
        Assert.Equal("OPTIONS", received.Method);
        Assert.Equal("https://beta-one.arkya.online", received.Headers["Origin"]);
        Assert.Equal("GET", received.Headers["Access-Control-Request-Method"]);
    }

    [Theory]
    [InlineData("AllowedHosts", "api.arkya.online;arkya.online;admin.arkya.online;*")]
    [InlineData("AllowedHosts", "api.arkya.online;arkya.online;admin.arkya.online;*.arkya.online")]
    [InlineData("Gateway:AdminEnabled", "false")]
    [InlineData("Gateway:FrontendEnabled", "false")]
    [InlineData("Gateway:PublicRealmsEnabled", "false")]
    [InlineData("Gateway:RealmBaseDomain", "*.arkya.online")]
    [InlineData("Gateway:RealmBaseDomain", "other.example")]
    [InlineData("Gateway:PublicRealmsAddress", "http://realms.internal/admin/")]
    public void Unsafe_realm_site_configuration_fails_at_startup(string key, string value)
    {
        var settings = Enabled("http://frontend.internal/", "http://admin.internal/", "http://realms.internal/");
        settings[key] = value;
        using var factory = new GatewayFactory("http://identity.internal/", overrides: settings);
        Assert.Throws<OptionsValidationException>(() => factory.Client());
    }
}
