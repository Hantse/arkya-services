using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Arkya.Admin.Realms;

namespace Arkya.Admin.Tests;

public sealed class RealmIslandOverviewTests
{
    private const string Path = "/Realms/terres-aube";
    private const string ImagePath = "/images/islands/starter-island.png";

    private static AdminFactory Factory(string status = "Ready")
    {
        var factory = new AdminFactory();
        factory.Settings["RealmsApi:Enabled"] = "true";
        factory.Realms.ConfigurationReadError = HttpStatusCode.ServiceUnavailable;
        factory.Realms.CommandsReadError = HttpStatusCode.ServiceUnavailable;
        var starter = new RealmIslandTopology("starter-island", 6, 1536, 1536, 0, 0, 3, 2);
        var second = new RealmIslandTopology("second-island", 4, 800, 1200, -200, 40, 1, 4);
        factory.Realms.Realm = factory.Realms.Realm with
        {
            Status = status,
            Topology = new([starter, second], 2, 1),
            TopologyRuntimeSupported = true,
            Servers =
            [
                IslandServer(starter, 0, new(0, 0, 512, 768)),
                IslandServer(starter, 1, new(512, 0, 1024, 768)),
                IslandServer(starter, 2, new(1024, 0, 1536, 768)),
                IslandServer(starter, 3, new(0, 768, 512, 1536)),
                IslandServer(starter, 4, new(512, 768, 1024, 1536)),
                IslandServer(starter, 5, new(1024, 768, 1536, 1536)),
                IslandServer(second, 0, new(-200, 40, 600, 340)),
                IslandServer(second, 1, new(-200, 340, 600, 640)),
                IslandServer(second, 2, new(-200, 640, 600, 940)),
                IslandServer(second, 3, new(-200, 940, 600, 1240)),
                new("instance-01", 7777, 1, 1, null) { Kind = "Instance" },
                new("instance-02", 7777, 0, 1, null) { Kind = "Instance" },
                new("raid-01", 7777, 1, 1, null) { Kind = "Raid" }
            ]
        };
        return factory;
    }

    private static RealmServer IslandServer(RealmIslandTopology plan, int index, AreaBounds bounds) =>
        new($"island-{plan.IslandId}-{index + 1:D2}", 7777, 1, 1, null)
        { Kind = "Island", IslandId = plan.IslandId, PartitionIndex = index, PartitionCount = plan.DedicatedCount, Bounds = bounds };

    [Fact]
    public async Task OverviewListsBothAreasAndPlacesTheFirstRowAtTheBottomOfTheMap()
    {
        await using var factory = Factory();
        var html = await Overview(factory);
        var links = IslandLinks(html);
        var decoded = WebUtility.HtmlDecode(html);
        Assert.Contains("Realm Areas", decoded);
        Assert.Contains("Choose an Area", decoded);
        Assert.Contains("Each Area is a complete map in the realm", decoded);
        Assert.Contains("1 partition = 1 dedicated server", decoded);
        Assert.DoesNotContain("Îles du royaume", decoded);
        Assert.Equal(2, links.Length);
        Assert.Contains("starter-island", Assert.Single(links, link => link.Contains("aria-current=\"true\"")));
        Assert.Contains("3 × 2", WebUtility.HtmlDecode(links[0]));
        Assert.Contains("1 × 4", WebUtility.HtmlDecode(links[1]));
        var svg = Map(html);
        Assert.Equal("0 0 1536 1536", (string?)svg.Attribute("viewBox"));
        var cells = Cells(svg);
        Assert.Equal(6, cells.Length);
        Assert.Equal(["A", "B", "C", "D", "E", "F"], cells.Select(cell => cell.Element("text")!.Value));
        AssertRect(cells[0], "0", "768", "512", "768");
        AssertRect(cells[1], "512", "768", "512", "768");
        AssertRect(cells[3], "0", "0", "512", "768");
        AssertRect(cells[5], "1024", "0", "512", "768");
        Assert.Contains("X → · Y ↑", WebUtility.HtmlDecode(html));
        Assert.Contains("Y = world Z", WebUtility.HtmlDecode(html));
        Assert.Contains("instance-01", html);
        Assert.Contains("raid-01", html);
    }

    [Fact]
    public async Task SelectingTheSecondAreaShowsOnlyItsFourPartitionsAndRefreshKeepsTheSelection()
    {
        await using var factory = Factory();
        var html = await Overview(factory, "second-island");
        var active = Assert.Single(IslandLinks(html), link => link.Contains("aria-current=\"true\""));
        Assert.Contains("island=second-island", active);
        var refresh = Assert.Single(Regex.Matches(html, @"<a\b[^>]*>.*?</a>", RegexOptions.Singleline).Select(match => match.Value),
            anchor => anchor.Contains("Refresh"));
        Assert.Contains("island=second-island", refresh);
        var svg = Map(html);
        Assert.Equal("0 0 800 1200", (string?)svg.Attribute("viewBox"));
        var cells = Cells(svg);
        Assert.Equal(4, cells.Length);
        AssertRect(cells[0], "0", "900", "800", "300");
        AssertRect(cells[3], "0", "0", "800", "300");
        Assert.All(cells, cell => Assert.Contains("island-second-island-", cell.Element("title")!.Value));
        Assert.Empty(svg.Elements("image"));
        Assert.Contains("Add an image for this Area to the catalogue", WebUtility.HtmlDecode(html));
    }

    [Theory]
    [InlineData("missing-island")]
    [InlineData("../Shared/_Layout")]
    [InlineData("<script>unsafe()</script>")]
    public async Task UnknownIslandSelectionFallsBackToTheFirstIslandWithoutRenderingUntrustedInput(string island)
    {
        await using var factory = Factory();
        var html = await Overview(factory, island);
        Assert.Contains("starter-island", Assert.Single(IslandLinks(html), link => link.Contains("aria-current=\"true\"")));
        Assert.Equal(6, Cells(Map(html)).Length);
        Assert.DoesNotContain(island, WebUtility.HtmlDecode(html));
        Assert.DoesNotContain("<script>unsafe", html);
        Assert.DoesNotContain("%3Cscript", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MapCoordinatesComeFromTheStoredServerBoundsInsteadOfRecomputingTheGrid()
    {
        await using var factory = Factory();
        factory.Realms.Realm = factory.Realms.Realm with
        {
            Servers = factory.Realms.Realm.Servers.Select(server => server.Id == "island-starter-island-01"
                ? server with { Bounds = new(37, 111, 499, 711) } : server).ToArray()
        };
        var html = await Overview(factory);
        var first = Cells(Map(html))[0];
        AssertRect(first, "37", "825", "462", "600");
        Assert.Contains("(37, 111) → (499, 711)", first.Element("title")!.Value);
        Assert.Contains("37 / 111", WebUtility.HtmlDecode(html));
        Assert.Contains("499 / 711", WebUtility.HtmlDecode(html));
    }

    [Fact]
    public async Task KnownIslandUsesTheReviewedLocalImageAndUnknownIslandDoesNotCreateAnImagePath()
    {
        await using var known = Factory();
        var knownHtml = await Overview(known);
        Assert.Equal(ImagePath, (string?)Assert.Single(Map(knownHtml).Elements("image")).Attribute("href"));
        Assert.Contains("src=\"" + ImagePath + "\"", IslandLinks(knownHtml)[0]);
        await using var unknown = Factory();
        var unknownHtml = await Overview(unknown, "second-island");
        Assert.Empty(Map(unknownHtml).Elements("image"));
        Assert.Contains("realm-island-thumbnail-empty", IslandLinks(unknownHtml)[1]);
        Assert.DoesNotContain("/images/islands/second-island", unknownHtml);
    }

    [Fact]
    public async Task IslandNamesAndServerIdsRemainEscapedAndCannotSelectAnExternalImage()
    {
        await using var factory = Factory();
        const string hostile = "<img src=x onerror=unsafe()>";
        var plan = new RealmIslandTopology(hostile, 1, GridColumns: 1, GridRows: 1);
        factory.Realms.Realm = factory.Realms.Realm with
        {
            Topology = new([plan], 0, 0),
            Servers = [IslandServer(plan, 0, new(0, 0, 1000, 1000)) with { Id = "<script>unsafe()</script>" }]
        };
        var html = await Overview(factory, hostile);
        Assert.DoesNotContain(hostile, html);
        Assert.DoesNotContain("<script>unsafe", html);
        Assert.Contains("&lt;img", html);
        Assert.Empty(Map(html).Elements("image"));
        Assert.Contains("<script>unsafe()</script>", Cells(Map(html))[0].Element("title")!.Value);
    }

    [Fact]
    public async Task PoolOnlyTopologyShowsCapacitiesWithoutInventingAnIslandOrMap()
    {
        await using var factory = Factory();
        factory.Realms.Realm = factory.Realms.Realm with
        {
            Topology = new([], 2, 1),
            Servers = factory.Realms.Realm.Servers.Where(server => server.Kind is "Instance" or "Raid").ToArray()
        };
        var html = await Overview(factory);
        var decoded = WebUtility.HtmlDecode(html);
        Assert.Contains("No Areas in this plan", decoded);
        Assert.Contains("Instance pool", decoded);
        Assert.Contains("Raid pool", decoded);
        Assert.Contains("instance-02", html);
        Assert.Contains("raid-01", html);
        Assert.DoesNotContain("realm-partition-map", html);
        Assert.Empty(IslandLinks(html));
    }

    [Theory]
    [InlineData("Draft", true)]
    [InlineData("Ready", false)]
    [InlineData("Pending", false)]
    [InlineData("Deploying", false)]
    [InlineData("Failed", false)]
    public async Task OnlyDraftRealmsExposeTheIslandEditingLink(string status, bool editable)
    {
        await using var factory = Factory(status);
        var html = await Overview(factory);
        Assert.Equal(editable, html.Contains("href=\"/CreateRealm?editRealmId=terres-aube\""));
        Assert.Equal(editable, WebUtility.HtmlDecode(html).Contains("Edit Areas and partitions"));
    }

    [Fact]
    public async Task FixedBetaProfileShowsOneStarterIslandZoneWithoutClaimingPartitionBounds()
    {
        await using var factory = Factory();
        factory.Realms.Realm = factory.Realms.Realm with
        {
            ProfileId = "beta-web", Topology = null,
            Servers = [new("island", 17843, 1, 1, "wss://play.example/island", "WebSocket")]
        };
        var html = await Overview(factory);
        var decoded = WebUtility.HtmlDecode(html);
        Assert.Single(IslandLinks(html));
        Assert.Contains("Starter Island", decoded);
        Assert.Contains("1 × 1", decoded);
        Assert.Contains("Fixed profile · one partition", decoded);
        var cell = Assert.Single(Cells(Map(html)));
        Assert.Equal("Partition A · island", cell.Element("title")!.Value.Trim());
        Assert.DoesNotContain("Partition coordinates", decoded);
        Assert.DoesNotContain("origine", decoded);
        Assert.DoesNotContain("(0, 0)", decoded);
        Assert.DoesNotContain("1 536 × 1 536", decoded);
    }

    [Theory]
    [InlineData("island-test", "island")]
    [InlineData("beta-web", "different-server")]
    public async Task OtherLegacyProfilesKeepTheirServerListWithoutInventingTheStarterIsland(string profile, string serverId)
    {
        await using var factory = Factory();
        factory.Realms.Realm = factory.Realms.Realm with
        {
            ProfileId = profile, Topology = null, Servers = [new(serverId, 7777, 1, 1, null)]
        };
        var html = await Overview(factory);
        Assert.Empty(IslandLinks(html));
        Assert.DoesNotContain("realm-partition-map", html);
        Assert.Contains(serverId, html);
    }

    [Theory]
    [InlineData("/CreateRealm", "'self'")]
    [InlineData("/CreateRealm/", "'self'")]
    [InlineData("/createrealm?editRealmId=terres-aube", "'self'")]
    [InlineData("/Realms", "'none'")]
    [InlineData(Path + "?section=overview", "'none'")]
    [InlineData(Path + "?section=settings", "'none'")]
    public async Task OnlyTheCreationPageAllowsItsLocalPreviewScript(string path, string scriptSource)
    {
        await using var factory = Factory("Draft");
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        using var response = await browser.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var policy = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("script-src " + scriptSource + ";", policy);
        Assert.DoesNotContain("unsafe-inline", policy);
        Assert.DoesNotContain("unsafe-eval", policy);
        Assert.Contains("frame-ancestors 'none'", policy);
    }

    private static async Task<string> Overview(AdminFactory factory, string? island = null)
    {
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync(Path + "?section=overview" + (island is null ? "" : "&island=" + Uri.EscapeDataString(island)));
        Assert.All(factory.Realms.Calls, call => Assert.Equal(HttpMethod.Get, call.Method));
        Assert.Equal(new[] { "/admin/realms/terres-aube", "/admin/realms/terres-aube/server-versions" }, factory.Realms.Calls.Select(call => call.Path));
        Assert.DoesNotContain("handler=", html);
        Assert.DoesNotContain("In-game control is unavailable", WebUtility.HtmlDecode(html));
        AdminFactory.NoSecrets(html);
        return html;
    }

    private static string[] IslandLinks(string html)
    {
        var nav = Regex.Match(html, @"<nav\b[^>]*class=""realm-island-list""[^>]*>.*?</nav>", RegexOptions.Singleline).Value;
        return Regex.Matches(nav, @"<a\b[^>]*>.*?</a>", RegexOptions.Singleline).Select(match => match.Value).ToArray();
    }

    private static XElement Map(string html) => XElement.Parse(Assert.Single(Regex.Matches(html,
        @"<svg\b[^>]*\brole=""img""[^>]*>.*?</svg>", RegexOptions.Singleline).Cast<Match>()).Value);
    private static XElement[] Cells(XElement svg) => svg.Elements("g").Where(element => (string?)element.Attribute("class") == "realm-partition-cell").ToArray();
    private static void AssertRect(XElement cell, string x, string y, string width, string height)
    {
        var rect = cell.Element("rect")!;
        Assert.Equal(x, (string?)rect.Attribute("x"));
        Assert.Equal(y, (string?)rect.Attribute("y"));
        Assert.Equal(width, (string?)rect.Attribute("width"));
        Assert.Equal(height, (string?)rect.Attribute("height"));
    }
}
