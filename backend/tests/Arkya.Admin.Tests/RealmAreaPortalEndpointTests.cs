using System.Net;
using System.Text.Json;
using Arkya.Admin.Realms;
using Arkya.ServiceDefaults;

namespace Arkya.Admin.Tests;

public sealed class RealmAreaPortalEndpointTests
{
    private static AdminFactory Factory()
    {
        var factory = new AdminFactory();
        factory.Settings["RealmsApi:Enabled"] = "true";
        factory.Realms.Realm = factory.Realms.Realm with
        {
            Topology = new([new("starter-island", 4, 1536, 1536, GridColumns: 2, GridRows: 2),
                new("camp-east", 4, 1536, 1536, GridColumns: 2, GridRows: 2) { ContentId = "starter-island" }], 0, 0)
            {
                AreaTransitions = [new("portal-east", "starter-island", "camp-east", new(100, 110, 120, 130),
                    new(15.125f, 45.5f, 20.75f), new(.6f, .8f)),
                    new("portal-west", "camp-east", "starter-island", new(200, 210, 220, 230), new(30, 40, 50))]
            }
        };
        return factory;
    }

    [Fact]
    public async Task DraftLoadsAndRoundTripsAllAreaContentAndPortalFieldsWithoutLosingNulls()
    {
        await using var factory = Factory();
        var expected = factory.Realms.Realm.Topology!;
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync("/CreateRealm?editRealmId=terres-aube");
        Assert.Equal("starter-island", AdminFactory.Input(html, "Input.Islands[1].ContentId"));
        Assert.Equal("portal-east", AdminFactory.Input(html, "Input.AreaTransitions[0].Id"));
        Assert.Equal("110", AdminFactory.Input(html, "Input.AreaTransitions[0].SourceMinZ"));
        Assert.Equal("15.125", AdminFactory.Input(html, "Input.AreaTransitions[0].TargetX"));
        Assert.Equal("0.6", AdminFactory.Input(html, "Input.AreaTransitions[0].ForwardX"));
        Assert.Contains("Automatic Area portals", html);
        var form = Form(AdminFactory.Input(html, "__RequestVerificationToken"));
        form["EditRealmId"] = "terres-aube";
        form["ExpectedRevision"] = "1";
        using var result = await browser.PostAsync("/CreateRealm?handler=SaveTopology", AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.Redirect, result.StatusCode);
        var body = Assert.Single(factory.Realms.Calls, call => call.Method == HttpMethod.Put).Body!.Value.GetProperty("topology");
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Assert.Equal(JsonSerializer.Serialize(expected, options), body.GetRawText());
        Assert.False(body.GetProperty("islands")[0].TryGetProperty("contentId", out _));
        Assert.False(body.GetProperty("areaTransitions")[1].TryGetProperty("targetForward", out _));
    }

    [Theory]
    [InlineData("Input.Islands[1].ContentId", "UPPERCASE")]
    [InlineData("Input.Islands[1].ContentId", "../map")]
    [InlineData("Input.AreaTransitions[0].Id", "bad id")]
    [InlineData("Input.AreaTransitions[1].Id", "portal-east")]
    [InlineData("Input.AreaTransitions[0].SourceAreaId", "missing-area")]
    [InlineData("Input.AreaTransitions[0].TargetAreaId", "starter-island")]
    [InlineData("Input.AreaTransitions[0].TargetAreaId", "missing-area")]
    [InlineData("Input.AreaTransitions[0].SourceMinX", "-1")]
    [InlineData("Input.AreaTransitions[0].SourceMaxX", "100")]
    [InlineData("Input.AreaTransitions[0].SourceMaxZ", "1537")]
    [InlineData("Input.AreaTransitions[0].SourceMinZ", "NaN")]
    [InlineData("Input.AreaTransitions[0].TargetX", "-1")]
    [InlineData("Input.AreaTransitions[0].TargetY", "100001")]
    [InlineData("Input.AreaTransitions[0].TargetZ", "Infinity")]
    [InlineData("Input.AreaTransitions[0].ForwardX", "2")]
    [InlineData("Input.AreaTransitions[0].ForwardX", "NaN")]
    public async Task InvalidPortalPlansStayInTheFormAndDoNotReachTheApi(string field, string value)
    {
        await using var factory = Factory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = Form(await AdminFactory.Token(browser, "/CreateRealm"));
        form[field] = value;
        using var result = await browser.PostAsync("/CreateRealm?handler=Create", AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var html = await result.Content.ReadAsStringAsync();
        Assert.Contains("validation-summary-errors", html);
        Assert.Equal(form["Input.Islands[1].ContentId"], AdminFactory.Input(html, "Input.Islands[1].ContentId"));
        Assert.Equal(form["Input.AreaTransitions[1].Id"], AdminFactory.Input(html, "Input.AreaTransitions[1].Id"));
        Assert.DoesNotContain(factory.Realms.Calls, call => call.Method != HttpMethod.Get);
        AdminFactory.NoSecrets(html);
    }

    [Fact]
    public async Task ZeroArrivalDirectionIsRejectedButOmittingItPreservesTheSourceDirection()
    {
        await using var factory = Factory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = Form(await AdminFactory.Token(browser, "/CreateRealm"));
        form["Input.AreaTransitions[0].ForwardX"] = "0";
        form["Input.AreaTransitions[0].ForwardZ"] = "0";
        using var rejected = await browser.PostAsync("/CreateRealm?handler=Create", AdminFactory.Form(form));
        Assert.Contains("arrival direction must be finite", WebUtility.HtmlDecode(await rejected.Content.ReadAsStringAsync()));
        Assert.DoesNotContain(factory.Realms.Calls, call => call.Method != HttpMethod.Get);
        form["Input.AreaTransitions[0].UseTargetForward"] = "false";
        using var accepted = await browser.PostAsync("/CreateRealm?handler=Create", AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.Redirect, accepted.StatusCode);
        Assert.Null(factory.Realms.Realm.Topology!.AreaTransitions![0].TargetForward);
    }

    [Fact]
    public async Task RowHandlersKeepAllUnrelatedContentAndPortalsWithoutSavingTheRealm()
    {
        await using var factory = Factory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = Form(await AdminFactory.Token(browser, "/CreateRealm"));
        using var added = await browser.PostAsync("/CreateRealm?handler=AddTransition", AdminFactory.Form(form));
        var html = await added.Content.ReadAsStringAsync();
        Assert.Equal("starter-island", AdminFactory.Input(html, "Input.Islands[1].ContentId"));
        Assert.Equal("portal-west", AdminFactory.Input(html, "Input.AreaTransitions[1].Id"));
        Assert.Contains("Input.AreaTransitions[2].Id", html);
        form["removeTransitionIndex"] = "0";
        using var removed = await browser.PostAsync("/CreateRealm?handler=RemoveTransition", AdminFactory.Form(form));
        html = await removed.Content.ReadAsStringAsync();
        Assert.Equal("portal-west", AdminFactory.Input(html, "Input.AreaTransitions[0].Id"));
        Assert.Equal("200", AdminFactory.Input(html, "Input.AreaTransitions[0].SourceMinX"));
        Assert.DoesNotContain("Input.AreaTransitions[1].Id", html);
        using var areaAdded = await browser.PostAsync("/CreateRealm?handler=AddIsland", AdminFactory.Form(form));
        html = await areaAdded.Content.ReadAsStringAsync();
        Assert.Equal("portal-east", AdminFactory.Input(html, "Input.AreaTransitions[0].Id"));
        form["removeIslandIndex"] = "0";
        using var areaRemoved = await browser.PostAsync("/CreateRealm?handler=RemoveIsland", AdminFactory.Form(form));
        html = await areaRemoved.Content.ReadAsStringAsync();
        Assert.Equal("starter-island", AdminFactory.Input(html, "Input.Islands[0].ContentId"));
        Assert.Equal("starter-island", AdminFactory.Input(html, "Input.AreaTransitions[0].SourceAreaId"));
        Assert.DoesNotContain(factory.Realms.Calls, call => call.Method != HttpMethod.Get);
    }

    [Theory]
    [InlineData("AddTransition")]
    [InlineData("RemoveTransition")]
    public async Task PortalRowMutationsRequireAntiforgery(string handler)
    {
        await using var factory = Factory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        using var result = await browser.PostAsync("/CreateRealm?handler=" + handler, AdminFactory.Form(new()));
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Empty(factory.Realms.Calls);
    }

    [Fact]
    public async Task FullSizedPlanWith32AreasAnd64PortalsFitsTheFormAndKeepsEveryEntry()
    {
        await using var factory = Factory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = Form(await AdminFactory.Token(browser, "/CreateRealm"));
        for (var index = 0; index < 32; index++) Area(form, index, index == 0 ? "starter-island" : index == 1 ? "camp-east" : $"area-{index:D2}");
        for (var index = 0; index < 64; index++) Portal(form, index, $"portal-{index:D2}", "starter-island", "camp-east", true);
        using var result = await browser.PostAsync("/CreateRealm?handler=Create", AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.Redirect, result.StatusCode);
        Assert.Equal(32, factory.Realms.Realm.Topology!.Islands.Length);
        Assert.Equal(64, factory.Realms.Realm.Topology.AreaTransitions!.Length);
    }

    [Fact]
    public async Task DetailsShowPortalDestinationsAndSharedMapContent()
    {
        await using var factory = Factory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = WebUtility.HtmlDecode(await browser.GetStringAsync("/Realms/terres-aube?island=camp-east"));
        Assert.Contains("Content: <span class=\"mono\">starter-island", html);
        Assert.Contains("Automatic Area portals", html);
        Assert.Contains("portal-east", html);
        Assert.Contains("15.125, 45.5, 20.75", html);
        Assert.Contains("Preserve player direction", html);
        Assert.Contains("/images/islands/starter-island.png", html);
    }

    [Fact]
    public void LegacyTopologySerializationDoesNotGainNullMetadataProperties()
    {
        var json = JsonSerializer.Serialize(new RealmTopology([new("starter-island", 4)], 0, 0),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("contentId", json);
        Assert.DoesNotContain("areaTransitions", json);
    }

    private static Dictionary<string, string> Form(string token)
    {
        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token, ["Input.RealmId"] = "terres-aube",
            ["Input.DisplayName"] = "Les Terres d’Aube", ["Input.TargetId"] = "test-cluster", ["Input.ProfileId"] = "island-test",
            ["Input.ConfigureTopology"] = "true", ["Input.InstanceDedicatedCount"] = "0", ["Input.RaidDedicatedCount"] = "0"
        };
        Area(form, 0, "starter-island"); Area(form, 1, "camp-east");
        Portal(form, 0, "portal-east", "starter-island", "camp-east", true);
        Portal(form, 1, "portal-west", "camp-east", "starter-island", false);
        foreach (var (field, value) in new[] { ("SourceMinX", "200"), ("SourceMinZ", "210"), ("SourceMaxX", "220"),
            ("SourceMaxZ", "230"), ("TargetX", "30"), ("TargetY", "40"), ("TargetZ", "50") })
            form["Input.AreaTransitions[1]." + field] = value;
        return form;
    }
    private static void Area(Dictionary<string, string> form, int index, string id)
    {
        foreach (var (field, value) in new[] { ("IslandId", id), ("ContentId", index == 0 ? "" : "starter-island"),
            ("DedicatedCount", "4"), ("UseGrid", "true"), ("GridColumns", "2"), ("GridRows", "2"),
            ("Width", "1536"), ("Depth", "1536"), ("OriginX", "0"), ("OriginZ", "0") }) form[$"Input.Islands[{index}].{field}"] = value;
    }
    private static void Portal(Dictionary<string, string> form, int index, string id, string source, string target, bool heading)
    {
        foreach (var (field, value) in new[] { ("Id", id), ("SourceAreaId", source), ("TargetAreaId", target),
            ("SourceMinX", "100"), ("SourceMinZ", "110"), ("SourceMaxX", "120"), ("SourceMaxZ", "130"),
            ("TargetX", "15.125"), ("TargetY", "45.5"), ("TargetZ", "20.75"),
            ("UseTargetForward", heading ? "true" : "false"), ("ForwardX", "0.6"), ("ForwardZ", "0.8") })
            form[$"Input.AreaTransitions[{index}].{field}"] = value;
    }
}
