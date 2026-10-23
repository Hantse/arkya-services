using System.Net;
using System.Text.Json;
using Arkya.Admin.Realms;

namespace Arkya.Admin.Tests;

public sealed class RealmsTopologyEndpointTests
{
    private static AdminFactory EnabledFactory()
    {
        var factory = new AdminFactory();
        factory.Settings["RealmsApi:Enabled"] = "true";
        return factory;
    }

    [Fact]
    public async Task CreationOffersAreasWithPartitionsAndSeparateInstanceAndRaidPools()
    {
        await using var factory = EnabledFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync("/CreateRealm");
        var decoded = WebUtility.HtmlDecode(html);
        Assert.Contains("Configure Areas, instances and raids", decoded);
        Assert.Contains("A realm can contain multiple Areas", decoded);
        Assert.Contains("Each Area is a complete, continuous map", decoded);
        Assert.Contains("Area ID", decoded);
        Assert.Contains("Add an Area", decoded);
        Assert.Contains("one Area partition = one dedicated server", decoded);
        Assert.Contains("32 Areas and 256 dedicated servers per realm", decoded);
        Assert.Equal("starter-island", AdminFactory.Input(html, "Input.Islands[0].IslandId"));
        Assert.Equal("4", AdminFactory.Input(html, "Input.Islands[0].DedicatedCount"));
        Assert.Equal("1536", AdminFactory.Input(html, "Input.Islands[0].Width"));
        Assert.Equal("1536", AdminFactory.Input(html, "Input.Islands[0].Depth"));
        Assert.Equal("0", AdminFactory.Input(html, "Input.Islands[0].OriginX"));
        Assert.Equal("0", AdminFactory.Input(html, "Input.Islands[0].OriginZ"));
        Assert.Contains("Input.InstanceDedicatedCount", html);
        Assert.Contains("Input.RaidDedicatedCount", html);
        Assert.Contains("deployment of this topology unavailable", WebUtility.HtmlDecode(html));
    }

    [Fact]
    public async Task CreationSendsTheValidatedPlanIncludingFutureIslandCoordinates()
    {
        await using var factory = EnabledFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = PlanForm(await AdminFactory.Token(browser, "/CreateRealm"));
        form["Input.Islands[1].OriginX"] = "-2000";
        form["Input.Islands[1].OriginZ"] = "500";
        form["Input.Islands[1].Args"] = "injected-runtime-flag";
        form["Input.Islands[1].Image"] = "injected-runtime-image";
        using var response = await browser.PostAsync("/CreateRealm?handler=Create", AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var call = Assert.Single(factory.Realms.Calls, call => call.Method == HttpMethod.Post);
        var submitted = call.Body!.Value.Deserialize<CreateRealmRequest>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.NotNull(submitted.Topology);
        Assert.Collection(submitted.Topology.Islands,
            island => Assert.Equal(new RealmIslandTopology("starter-island", 4, 1000, 1000, 0, 0), island),
            island => Assert.Equal(new RealmIslandTopology("future-island", 3, 2400, 1200, -2000, 500), island));
        Assert.Equal(2, submitted.Topology.InstanceDedicatedCount);
        Assert.Equal(1, submitted.Topology.RaidDedicatedCount);
        Assert.DoesNotContain("injected-runtime", call.Body.Value.GetRawText());
    }

    [Theory]
    [InlineData("Input.Islands[0].IslandId", "../secret")]
    [InlineData("Input.Islands[0].IslandId", "UPPERCASE")]
    [InlineData("Input.Islands[0].DedicatedCount", "0")]
    [InlineData("Input.Islands[0].DedicatedCount", "65")]
    [InlineData("Input.Islands[0].Width", "0")]
    [InlineData("Input.Islands[0].Width", "0.5")]
    [InlineData("Input.Islands[0].Depth", "1000001")]
    [InlineData("Input.Islands[0].Width", "NaN")]
    [InlineData("Input.Islands[0].OriginX", "10000001")]
    [InlineData("Input.Islands[0].OriginZ", "-10000001")]
    [InlineData("Input.Islands[1].IslandId", "starter-island")]
    [InlineData("Input.InstanceDedicatedCount", "-1")]
    [InlineData("Input.RaidDedicatedCount", "65")]
    public async Task InvalidPlansDoNotReachTheCreateApi(string field, string value)
    {
        await using var factory = EnabledFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = PlanForm(await AdminFactory.Token(browser, "/CreateRealm"));
        form[field] = value;
        using var response = await browser.PostAsync("/CreateRealm?handler=Create", AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("validation-summary-errors", html);
        Assert.DoesNotContain(factory.Realms.Calls, call => call.Method != HttpMethod.Get);
        AdminFactory.NoSecrets(html);
    }

    [Fact]
    public async Task PlanTotalCannotExceedTheRealmBudget()
    {
        await using var factory = EnabledFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = PlanForm(await AdminFactory.Token(browser, "/CreateRealm"));
        for (var index = 0; index < 5; index++)
        {
            form[$"Input.Islands[{index}].IslandId"] = "island-" + index;
            form[$"Input.Islands[{index}].DedicatedCount"] = "64";
        }
        using var response = await browser.PostAsync("/CreateRealm?handler=Create", AdminFactory.Form(form));
        Assert.Contains("256 dedicated servers", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        Assert.DoesNotContain(factory.Realms.Calls, call => call.Method != HttpMethod.Get);
    }

    [Fact]
    public async Task AddAndRemoveIslandKeepTheOtherCoordinatesWithoutCreatingARealm()
    {
        await using var factory = EnabledFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = PlanForm(await AdminFactory.Token(browser, "/CreateRealm"));
        form["Input.Islands[1].OriginX"] = "-2000";
        using var added = await browser.PostAsync("/CreateRealm?handler=AddIsland", AdminFactory.Form(form));
        var html = await added.Content.ReadAsStringAsync();
        Assert.Equal("-2000", AdminFactory.Input(html, "Input.Islands[1].OriginX"));
        Assert.Equal("", AdminFactory.Input(html, "Input.Islands[2].IslandId"));
        form["removeIslandIndex"] = "0";
        using var removed = await browser.PostAsync("/CreateRealm?handler=RemoveIsland", AdminFactory.Form(form));
        html = await removed.Content.ReadAsStringAsync();
        Assert.Equal("future-island", AdminFactory.Input(html, "Input.Islands[0].IslandId"));
        Assert.Equal("-2000", AdminFactory.Input(html, "Input.Islands[0].OriginX"));
        Assert.DoesNotContain(factory.Realms.Calls, call => call.Method != HttpMethod.Get);
    }

    [Theory]
    [InlineData("AddIsland")]
    [InlineData("RemoveIsland")]
    public async Task EditingIslandRowsRequiresAntiforgery(string handler)
    {
        await using var factory = EnabledFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        using var response = await browser.PostAsync("/CreateRealm?handler=" + handler, AdminFactory.Form(new()));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.Realms.Calls);
    }

    [Fact]
    public async Task RealmDetailsShowPartitionBoundsPoolsAndUnsupportedDeployment()
    {
        await using var factory = EnabledFactory();
        factory.Realms.Realm = factory.Realms.Realm with
        {
            Topology = new([new("starter-island", 4)], 2, 1),
            Servers = [
                new("starter-island-00", 7777, 0, 0, null) { Kind = "Island", IslandId = "starter-island", PartitionIndex = 0, PartitionCount = 4, Bounds = new(0, 0, 500, 500) },
                new("starter-island-03", 7777, 0, 0, null) { Kind = "Island", IslandId = "starter-island", PartitionIndex = 3, PartitionCount = 4, Bounds = new(500, 500, 1000, 1000) }
            ]
        };
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = WebUtility.HtmlDecode(await browser.GetStringAsync("/Realms/terres-aube"));
        Assert.Contains("Instance pool", html);
        Assert.Contains("Raid pool", html);
        Assert.Contains("Partition bounds (X / Z)", html);
        Assert.Contains("realm-partition-map", html);
        Assert.Contains("realm-partition-cell", html);
        Assert.Contains("role=\"img\"", html);
        Assert.Contains("starter-island-00</th><td>0 / 0</td><td>500 / 500</td>", html);
        Assert.Contains("starter-island-03</th><td>500 / 500</td><td>1000 / 1000</td>", html);
        Assert.Contains("Deployment is blocked", html);
        var maintenance = WebUtility.HtmlDecode(await browser.GetStringAsync("/Realms/terres-aube?section=maintenance"));
        Assert.Matches("<button[^>]+disabled=\"disabled\"[^>]*>Deploy this realm</button>", maintenance);
        AdminFactory.NoSecrets(html);
    }

    [Fact]
    public async Task DraftEditLoadsItsExistingPlanAndSavesOnlyTheDisplayedRevisionProfileAndTopology()
    {
        await using var factory = EnabledFactory();
        factory.Realms.Realm = factory.Realms.Realm with { Topology = new([new("starter-island", 4, 1200, 800, -200, 100)], 2, 1) };
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync("/CreateRealm?editRealmId=terres-aube");
        Assert.Equal("1", AdminFactory.Input(html, "ExpectedRevision"));
        Assert.Equal("1200", AdminFactory.Input(html, "Input.Islands[0].Width"));
        Assert.Equal("-200", AdminFactory.Input(html, "Input.Islands[0].OriginX"));
        var form = PlanForm(AdminFactory.Input(html, "__RequestVerificationToken"));
        form["EditRealmId"] = "terres-aube";
        form["ExpectedRevision"] = "1";
        form["Input.TargetId"] = "cannot-change-target";
        form["Input.RealmId"] = "cannot-change-identity";
        using var response = await browser.PostAsync("/CreateRealm?handler=SaveTopology", AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var call = Assert.Single(factory.Realms.Calls, call => call.Method != HttpMethod.Get);
        Assert.Equal(HttpMethod.Put, call.Method);
        Assert.Equal("/admin/realms/terres-aube/topology", call.Path);
        var body = call.Body!.Value;
        Assert.Equal(3, body.EnumerateObject().Count());
        Assert.Equal(1, body.GetProperty("expectedRevision").GetInt64());
        Assert.Equal("island-test", body.GetProperty("profileId").GetString());
        Assert.Equal(2, body.GetProperty("topology").GetProperty("islands").GetArrayLength());
        Assert.Equal("terres-aube", factory.Realms.Realm.RealmId);
        Assert.Equal("test-cluster", factory.Realms.Realm.TargetId);
    }

    [Fact]
    public async Task DraftCanRestoreTheLegacyProfileAndKeepItsIdentity()
    {
        await using var factory = EnabledFactory();
        factory.Realms.Realm = factory.Realms.Realm with { Topology = new([new("starter-island", 4)], 2, 1) };
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = PlanForm(await AdminFactory.Token(browser, "/CreateRealm?editRealmId=terres-aube"));
        form["EditRealmId"] = "terres-aube";
        form["ExpectedRevision"] = "1";
        form["Input.ConfigureTopology"] = "false";
        using var response = await browser.PostAsync("/CreateRealm?handler=SaveTopology", AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var call = Assert.Single(factory.Realms.Calls, call => call.Method != HttpMethod.Get);
        Assert.Equal(JsonValueKind.Null, call.Body!.Value.GetProperty("topology").ValueKind);
        Assert.Null(factory.Realms.Realm.Topology);
    }

    [Fact]
    public async Task StaleDraftEditShowsConflictWithoutWritingAnotherRevision()
    {
        await using var factory = EnabledFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = PlanForm(await AdminFactory.Token(browser, "/CreateRealm?editRealmId=terres-aube"));
        form["EditRealmId"] = "terres-aube";
        form["ExpectedRevision"] = "0";
        using var response = await browser.PostAsync("/CreateRealm?handler=SaveTopology", AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("has changed", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        Assert.Equal(1, factory.Realms.Realm.Revision);
    }

    [Fact]
    public async Task OnlyDraftsOfferTopologyEditing()
    {
        await using var factory = EnabledFactory();
        factory.Realms.Realm = factory.Realms.Realm with { Status = "Ready" };
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        Assert.DoesNotContain("Edit plan", WebUtility.HtmlDecode(await browser.GetStringAsync("/Realms/terres-aube?section=maintenance")));
        using var response = await browser.GetAsync("/CreateRealm?editRealmId=terres-aube");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PoolOnlyPlansDoNotRecreateAnIslandFromDefaults()
    {
        await using var factory = EnabledFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = PlanForm(await AdminFactory.Token(browser, "/CreateRealm"));
        foreach (var key in form.Keys.Where(key => key.StartsWith("Input.Islands[", StringComparison.Ordinal)).ToArray()) form.Remove(key);
        using var response = await browser.PostAsync("/CreateRealm?handler=Create", AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var call = Assert.Single(factory.Realms.Calls, call => call.Method == HttpMethod.Post);
        Assert.Equal(0, call.Body!.Value.GetProperty("topology").GetProperty("islands").GetArrayLength());
    }

    [Fact]
    public async Task SavingDraftTopologyRequiresAntiforgery()
    {
        await using var factory = EnabledFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        using var response = await browser.PostAsync("/CreateRealm?handler=SaveTopology", AdminFactory.Form(new()));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.Realms.Calls);
    }

    private static Dictionary<string, string> PlanForm(string token) => new()
    {
        ["__RequestVerificationToken"] = token,
        ["Input.RealmId"] = "terres-aube", ["Input.DisplayName"] = "Les Terres d’Aube",
        ["Input.TargetId"] = "test-cluster", ["Input.ProfileId"] = "island-test",
        ["Input.ConfigureTopology"] = "true",
        ["Input.Islands[0].IslandId"] = "starter-island", ["Input.Islands[0].DedicatedCount"] = "4",
        ["Input.Islands[0].Width"] = "1000", ["Input.Islands[0].Depth"] = "1000",
        ["Input.Islands[0].OriginX"] = "0", ["Input.Islands[0].OriginZ"] = "0",
        ["Input.Islands[1].IslandId"] = "future-island", ["Input.Islands[1].DedicatedCount"] = "3",
        ["Input.Islands[1].Width"] = "2400", ["Input.Islands[1].Depth"] = "1200",
        ["Input.Islands[1].OriginX"] = "0", ["Input.Islands[1].OriginZ"] = "0",
        ["Input.InstanceDedicatedCount"] = "2", ["Input.RaidDedicatedCount"] = "1"
    };
}
