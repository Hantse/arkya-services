using System.Net;
using System.Net.Http.Json;
using Arkya.Admin.Realms;
using Arkya.ServiceDefaults;

namespace Arkya.Admin.Tests;

public sealed class RealmWorldMapEndpointTests
{
    private const string Path = "/Realms/terres-aube";
    private const string Handler = Path + "?handler=WorldMap";

    private static AdminFactory Factory()
    {
        var factory = new AdminFactory();
        factory.Settings["RealmsApi:Enabled"] = "true";
        factory.Realms.Realm = factory.Realms.Realm with
        {
            Status = "Ready", ProfileId = "beta-web", Topology = null,
            Servers = [new("island", 17943, 1, 1, null, "WebSocket")]
        };
        return factory;
    }

    [Fact]
    public async Task MapShowsSelectedAreaTerrainFiltersAndLocalScriptWithoutReadingPersistedCharacterPositions()
    {
        await using var factory = Factory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        using var response = await browser.GetAsync(Path + "?section=map&island=starter-island");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-realm-section=\"map\"", html);
        Assert.Contains("Live world map", html);
        Assert.Contains("data-island-id=\"starter-island\"", html);
        Assert.Contains("/images/islands/starter-island.png", html);
        Assert.Contains("data-map-kind=\"player\"", html);
        Assert.Contains("data-map-kind=\"npc\"", html);
        Assert.Contains("data-map-kind=\"creature\"", html);
        Assert.Contains("Entity details", html);
        Assert.Contains("handler=WorldMap", html);
        Assert.Contains("/realm-map.js?v=", html);
        Assert.Contains("/realm-map.css?v=", html);
        Assert.Contains("?section=map&amp;island=starter-island", html);
        Assert.Equal(new[] { "/admin/realms/terres-aube" }, factory.Realms.Calls.Select(call => call.Path));
        Assert.Contains("script-src 'self';", Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
        Assert.True(response.Headers.CacheControl!.NoStore);
        AdminFactory.NoSecrets(html);
    }

    [Fact]
    public async Task ProtectedHandlerForwardsOnlyThePrivateMapDtoAndKeepsNamesAsData()
    {
        await using var factory = Factory();
        var now = DateTimeOffset.UtcNow;
        factory.Realms.WorldMap = new("terres-aube", now, 2, 10,
            [new("island", "starter-island", now, "live", true, 4,
                [new("player-1", "player", "<img src=x onerror=alert(1)>", 12, 3, 45, 7),
                 new("npc-1", "npc", "Town resident", 22, 2, 30), new("creature-1", "creature", "Wolf", 20, 2, 35)])]);
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        using var response = await browser.GetAsync(Handler);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        Assert.True(response.Headers.CacheControl!.NoStore);
        var map = await response.Content.ReadFromJsonAsync<RealmWorldMap>();
        Assert.Equal("terres-aube", map!.RealmId);
        Assert.Equal(3, Assert.Single(map.Servers).Entities.Length);
        Assert.Equal(7, map.Servers[0].Entities[0].AuthorityEpoch);
        Assert.Contains("<img", map.Servers[0].Entities[0].Name);
        Assert.Equal(new[] { "/admin/realms/terres-aube/world-map" }, factory.Realms.Calls.Select(call => call.Path));
        Assert.All(factory.Realms.Calls, call => Assert.Equal(HttpMethod.Get, call.Method));
        AdminFactory.NoSecrets(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task HandlerRequiresAdministratorSessionAndEnabledRealmConnection()
    {
        await using var factory = Factory();
        using var browser = factory.Browser();
        Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync(Handler)).StatusCode);
        Assert.Empty(factory.Realms.Calls);
        await AdminFactory.Login(browser);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync("/Realms/bad!id?handler=WorldMap")).StatusCode);
        Assert.Empty(factory.Realms.Calls);
        await using var disabled = Factory();
        disabled.Settings["RealmsApi:Enabled"] = "false";
        using var disabledBrowser = disabled.Browser();
        await AdminFactory.Login(disabledBrowser);
        Assert.Equal(HttpStatusCode.NotFound, (await disabledBrowser.GetAsync(Handler)).StatusCode);
        Assert.Empty(disabled.Realms.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.Unauthorized, HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.NotFound, HttpStatusCode.NotFound)]
    public async Task HandlerHidesPrivateDependencyFailures(HttpStatusCode upstream, HttpStatusCode expected)
    {
        await using var factory = Factory();
        factory.Realms.WorldMapReadError = upstream;
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        using var response = await browser.GetAsync(Handler);
        Assert.Equal(expected, response.StatusCode);
        AdminFactory.NoSecrets(await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MalformedOrOtherRealmMapIsRejected(bool invalidJson)
    {
        await using var factory = Factory();
        factory.Realms.WorldMapInvalidJson = invalidJson;
        factory.Realms.WorldMap = factory.Realms.WorldMap with { RealmId = "another-realm" };
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        using var response = await browser.GetAsync(Handler);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        AdminFactory.NoSecrets(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task DedicatedHostServesItsScriptAndKeepsJsonWithinItsRealm()
    {
        await using var factory = Factory();
        factory.Settings["RealmSites:Enabled"] = "true";
        factory.Settings["AllowedHosts"] = "admin.arkya.online;*.admin.arkya.online";
        using var browser = factory.Browser();
        browser.BaseAddress = new Uri("https://admin.arkya.online");
        await AdminFactory.Login(browser);
        const string dedicated = "https://terres-aube.admin.arkya.online";
        using var map = await browser.GetAsync(dedicated + "/?section=map");
        Assert.Equal(HttpStatusCode.OK, map.StatusCode);
        Assert.Contains("script-src 'self';", Assert.Single(map.Headers.GetValues("Content-Security-Policy")));
        using var script = await browser.GetAsync(dedicated + "/realm-map.js");
        Assert.Equal(HttpStatusCode.OK, script.StatusCode);
        Assert.Null(script.Headers.Location);
        Assert.Contains("data-world-map", await script.Content.ReadAsStringAsync());
        factory.Realms.Calls.Clear();
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync(dedicated + "/?handler=WorldMap")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync(dedicated + "/Realms/another-realm?handler=WorldMap")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync(dedicated + "/?handler=WorldMap&realmId=another-realm")).StatusCode);
        Assert.Single(factory.Realms.Calls);
    }

    [Fact]
    public async Task UnalignedMapUsesCoordinatesAndOtherSectionsDoNotLoadTheLiveScript()
    {
        await using var factory = Factory();
        factory.Realms.Realm = factory.Realms.Realm with
        {
            Topology = new([new("island-a", 1, 200, 100, 20, -50, 1, 1) { ContentId = "starter-island" }], 0, 0),
            Servers = [new("area-one", 7777, 1, 1, null) { IslandId = "island-a", PartitionIndex = 0 }]
        };
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync(Path + "?section=map");
        Assert.Contains("data-origin-x=\"20\"", html);
        Assert.Contains("data-origin-z=\"-50\"", html);
        Assert.DoesNotContain("<image", html);
        Assert.Contains("no aligned terrain image", html);
        using var overview = await browser.GetAsync(Path);
        Assert.DoesNotContain("/realm-map.js", await overview.Content.ReadAsStringAsync());
        Assert.Contains("script-src 'none';", Assert.Single(overview.Headers.GetValues("Content-Security-Policy")));
    }
}
