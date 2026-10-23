using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Arkya.Admin.Realms;

namespace Arkya.Admin.Tests;

public sealed class RealmCreateGridEndpointTests
{
    private static AdminFactory Factory()
    {
        var factory = new AdminFactory();
        factory.Settings["RealmsApi:Enabled"] = "true";
        return factory;
    }

    [Fact]
    public async Task NewIslandShowsItsLocalImageAndTwoByTwoGridBeforeAnyScriptRuns()
    {
        await using var factory = Factory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync("/CreateRealm");
        Assert.Equal("2", AdminFactory.Input(html, "Input.Islands[0].GridColumns"));
        Assert.Equal("2", AdminFactory.Input(html, "Input.Islands[0].GridRows"));
        Assert.Equal("4", AdminFactory.Input(html, "Input.Islands[0].DedicatedCount"));
        Assert.Equal("1536", AdminFactory.Input(html, "Input.Islands[0].Width"));
        Assert.Equal("1536", AdminFactory.Input(html, "Input.Islands[0].Depth"));
        Assert.Matches("<input(?=[^>]*name=\"Input.Islands\\[0\\].UseGrid\")(?=[^>]*checked=\"checked\")[^>]*>", html);
        Assert.Matches("<img[^>]*src=\"/images/islands/starter-island.png\"[^>]*>", html);
        Assert.Contains("Colonnes X", html);
        Assert.Contains("Lignes Y", html);
        Assert.Contains("2 × 2 = 4 dédié(s)", WebUtility.HtmlDecode(html));
        Assert.Contains("aria-live=\"polite\"", html);
        Assert.Contains("data-island-image-note", html);
        var grid = Regex.Match(html, "<tbody data-grid-cells>(.*?)</tbody>", RegexOptions.Singleline).Groups[1].Value;
        Assert.Equal(4, Regex.Matches(grid, "<td>").Count);
        Assert.True(grid.IndexOf("<span>C</span>", StringComparison.Ordinal) < grid.IndexOf("<span>A</span>", StringComparison.Ordinal));
        Assert.Matches("<script src=\"/realm-create-islands.js[^\"]*\" defer></script>", html);
        Assert.Contains("/realm-create-islands.css", html);
        Assert.DoesNotContain("onclick=", html);
        AdminFactory.NoSecrets(html);
    }

    [Fact]
    public async Task CreationDerivesDedicatedCountFromTheGridAndSendsOnlyTheValidatedPlan()
    {
        await using var factory = Factory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = GridForm(await AdminFactory.Token(browser, "/CreateRealm"));
        form["Input.Islands[0].DedicatedCount"] = "999999";
        form["Input.Islands[0].Image"] = "https://untrusted.invalid/island.png";
        using var response = await browser.PostAsync("/CreateRealm?handler=Create", AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var call = Assert.Single(factory.Realms.Calls, call => call.Method == HttpMethod.Post);
        var request = call.Body!.Value.Deserialize<CreateRealmRequest>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var island = Assert.Single(request.Topology!.Islands);
        Assert.Equal(3, island.GridColumns);
        Assert.Equal(2, island.GridRows);
        Assert.Equal(6, island.DedicatedCount);
        Assert.DoesNotContain("untrusted", call.Body.Value.GetRawText());
        Assert.DoesNotContain("useGrid", call.Body.Value.GetRawText());
    }

    [Theory]
    [InlineData("GridColumns", "")]
    [InlineData("GridRows", "")]
    [InlineData("GridColumns", "0")]
    [InlineData("GridRows", "-1")]
    [InlineData("GridColumns", "65")]
    [InlineData("GridRows", "9999999999999999999")]
    [InlineData("GridColumns", "2.5")]
    [InlineData("GridRows", "NaN")]
    [InlineData("GridColumns", "33")]
    public async Task InvalidGridDoesNotReachTheCreateApi(string field, string value)
    {
        await using var factory = Factory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = GridForm(await AdminFactory.Token(browser, "/CreateRealm"));
        form["Input.Islands[0]." + field] = value;
        using var response = await browser.PostAsync("/CreateRealm?handler=Create", AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("validation-summary-errors", html);
        Assert.DoesNotContain(factory.Realms.Calls, call => call.Method != HttpMethod.Get);
        AdminFactory.NoSecrets(html);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 64)]
    [InlineData(64, 1)]
    [InlineData(8, 8)]
    public async Task ValidGridBoundariesAreAccepted(int columns, int rows)
    {
        await using var factory = Factory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = GridForm(await AdminFactory.Token(browser, "/CreateRealm"));
        form["Input.Islands[0].GridColumns"] = columns.ToString();
        form["Input.Islands[0].GridRows"] = rows.ToString();
        using var response = await browser.PostAsync("/CreateRealm?handler=Create", AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(columns * rows, Assert.Single(factory.Realms.Realm.Topology!.Islands).DedicatedCount);
    }

    [Fact]
    public async Task LegacyDraftKeepsItsCountAndNullGridUntilAnExplicitChoice()
    {
        await using var factory = Factory();
        factory.Realms.Realm = factory.Realms.Realm with { Topology = new([new("starter-island", 3, 1200, 800)], 0, 0) };
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync("/CreateRealm?editRealmId=terres-aube");
        Assert.Equal("", AdminFactory.Input(html, "Input.Islands[0].GridColumns"));
        Assert.Equal("", AdminFactory.Input(html, "Input.Islands[0].GridRows"));
        Assert.Equal("3", AdminFactory.Input(html, "Input.Islands[0].DedicatedCount"));
        Assert.DoesNotMatch("<input(?=[^>]*name=\"Input.Islands\\[0\\].UseGrid\")(?=[^>]*checked=\"checked\")[^>]*>", html);
        Assert.Contains("Illustration · dimensions du plan différentes de la vue", WebUtility.HtmlDecode(html));
        var form = GridForm(AdminFactory.Input(html, "__RequestVerificationToken"));
        form["EditRealmId"] = "terres-aube";
        form["ExpectedRevision"] = "1";
        form["Input.Islands[0].UseGrid"] = "false";
        form["Input.Islands[0].DedicatedCount"] = "3";
        form["Input.Islands[0].Width"] = "1200";
        form["Input.Islands[0].Depth"] = "800";
        using var response = await browser.PostAsync("/CreateRealm?handler=SaveTopology", AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var saved = Assert.Single(factory.Realms.Realm.Topology!.Islands);
        Assert.Equal(3, saved.DedicatedCount);
        Assert.Null(saved.GridColumns);
        Assert.Null(saved.GridRows);
        Assert.Equal(1200, saved.Width);
        Assert.Equal(800, saved.Depth);
    }

    [Fact]
    public async Task DraftLoadsItsExplicitGridAndCanChooseAnotherWithTheDisplayedRevision()
    {
        await using var factory = Factory();
        factory.Realms.Realm = factory.Realms.Realm with { Topology = new([new("starter-island", 8, GridColumns: 4, GridRows: 2)], 0, 0) };
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync("/CreateRealm?editRealmId=terres-aube");
        Assert.Equal("4", AdminFactory.Input(html, "Input.Islands[0].GridColumns"));
        Assert.Equal("2", AdminFactory.Input(html, "Input.Islands[0].GridRows"));
        var form = GridForm(AdminFactory.Input(html, "__RequestVerificationToken"));
        form["EditRealmId"] = "terres-aube";
        form["ExpectedRevision"] = "1";
        using var response = await browser.PostAsync("/CreateRealm?handler=SaveTopology", AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var saved = Assert.Single(factory.Realms.Realm.Topology!.Islands);
        Assert.Equal(3, saved.GridColumns);
        Assert.Equal(2, saved.GridRows);
        Assert.Equal(6, saved.DedicatedCount);
    }

    [Fact]
    public async Task AddingAndRemovingRowsPreservesGridValuesAndAddsAOneByOneIsland()
    {
        await using var factory = Factory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = GridForm(await AdminFactory.Token(browser, "/CreateRealm"));
        using var added = await browser.PostAsync("/CreateRealm?handler=AddIsland", AdminFactory.Form(form));
        var html = await added.Content.ReadAsStringAsync();
        Assert.Equal("3", AdminFactory.Input(html, "Input.Islands[0].GridColumns"));
        Assert.Equal("6", AdminFactory.Input(html, "Input.Islands[0].DedicatedCount"));
        Assert.Equal("1", AdminFactory.Input(html, "Input.Islands[1].GridColumns"));
        Assert.Equal("1", AdminFactory.Input(html, "Input.Islands[1].GridRows"));
        foreach (var field in new[] { "IslandId", "UseGrid", "GridColumns", "GridRows", "DedicatedCount", "Width", "Depth", "OriginX", "OriginZ" })
            form["Input.Islands[1]." + field] = form["Input.Islands[0]." + field];
        form["Input.Islands[1].IslandId"] = "future-island";
        form["Input.Islands[1].GridColumns"] = "4";
        form["Input.Islands[1].GridRows"] = "3";
        form["removeIslandIndex"] = "0";
        using var removed = await browser.PostAsync("/CreateRealm?handler=RemoveIsland", AdminFactory.Form(form));
        html = await removed.Content.ReadAsStringAsync();
        Assert.Equal("future-island", AdminFactory.Input(html, "Input.Islands[0].IslandId"));
        Assert.Equal("4", AdminFactory.Input(html, "Input.Islands[0].GridColumns"));
        Assert.Equal("3", AdminFactory.Input(html, "Input.Islands[0].GridRows"));
        Assert.Equal("12", AdminFactory.Input(html, "Input.Islands[0].DedicatedCount"));
        Assert.DoesNotContain("Input.Islands[1].", html);
        Assert.DoesNotContain(factory.Realms.Calls, call => call.Method != HttpMethod.Get);
    }

    [Theory]
    [InlineData("future-island")]
    [InlineData("https://untrusted.invalid/island.png")]
    public async Task UnknownIdentifiersNeverBecomeImagePaths(string islandId)
    {
        await using var factory = Factory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = GridForm(await AdminFactory.Token(browser, "/CreateRealm"));
        form["Input.Islands[0].IslandId"] = islandId;
        form["Input.DisplayName"] = "";
        using var response = await browser.PostAsync("/CreateRealm?handler=Create", AdminFactory.Form(form));
        var html = await response.Content.ReadAsStringAsync();
        var image = Regex.Match(html, "<img[^>]*data-island-image[^>]*>").Value;
        Assert.NotEmpty(image);
        Assert.DoesNotContain("src=", image);
        Assert.Contains("Visuel indisponible pour cette Area", WebUtility.HtmlDecode(html));
        Assert.Contains("Aucune image de catalogue", WebUtility.HtmlDecode(html));
        Assert.DoesNotContain(factory.Realms.Calls, call => call.Method != HttpMethod.Get);
    }

    private static Dictionary<string, string> GridForm(string token) => new()
    {
        ["__RequestVerificationToken"] = token,
        ["Input.RealmId"] = "terres-aube", ["Input.DisplayName"] = "Les Terres d’Aube",
        ["Input.TargetId"] = "test-cluster", ["Input.ProfileId"] = "island-test",
        ["Input.ConfigureTopology"] = "true", ["Input.Islands[0].IslandId"] = "starter-island",
        ["Input.Islands[0].UseGrid"] = "true", ["Input.Islands[0].DedicatedCount"] = "4",
        ["Input.Islands[0].GridColumns"] = "3", ["Input.Islands[0].GridRows"] = "2",
        ["Input.Islands[0].Width"] = "1536", ["Input.Islands[0].Depth"] = "1536",
        ["Input.Islands[0].OriginX"] = "0", ["Input.Islands[0].OriginZ"] = "0",
        ["Input.InstanceDedicatedCount"] = "0", ["Input.RaidDedicatedCount"] = "0"
    };
}
