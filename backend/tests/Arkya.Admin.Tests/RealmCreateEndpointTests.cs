using System.Net;

namespace Arkya.Admin.Tests;

public sealed class RealmCreateEndpointTests
{
    private static AdminFactory Factory()
    {
        var factory = new AdminFactory();
        factory.Settings["RealmsApi:Enabled"] = "true";
        return factory;
    }

    [Fact]
    public async Task ReusableTargetSelectsTheDestinationWithoutReservingTheRealmIdentity()
    {
        await using var factory = Factory();
        factory.Realms.Options = factory.Realms.Options with
        {
            Targets = [new("beta-web", "Serveur principal", "arkya-beta-realm")]
        };
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync("/CreateRealm");
        Assert.Equal("", AdminFactory.Input(html, "Input.RealmId"));
        Assert.Equal("", AdminFactory.Input(html, "Input.DisplayName"));
        Assert.DoesNotMatch("<input(?=[^>]*name=\"Input.ConfigureTopology\")(?=[^>]*checked)[^>]*>", html);
        Assert.Matches("<option(?=[^>]*selected=\"selected\")(?=[^>]*value=\"beta-web\")[^>]*>", html);
        var token = AdminFactory.Input(html, "__RequestVerificationToken");
        using var result = await browser.PostAsync("/CreateRealm?handler=Create", AdminFactory.Form(new()
        {
            ["__RequestVerificationToken"] = token,
            ["Input.RealmId"] = "nouveau-monde", ["Input.DisplayName"] = "Nouveau monde",
            ["Input.TargetId"] = "beta-web", ["Input.ProfileId"] = "island-test",
            ["Input.ConfigureTopology"] = "false"
        }));
        Assert.Equal(HttpStatusCode.Redirect, result.StatusCode);
        Assert.Equal("/Realms/nouveau-monde", result.Headers.Location!.OriginalString);
        var call = Assert.Single(factory.Realms.Calls, call => call.Method == HttpMethod.Post);
        Assert.Equal("nouveau-monde", call.Body!.Value.GetProperty("realmId").GetString());
        Assert.Equal("beta-web", call.Body.Value.GetProperty("targetId").GetString());
    }

    [Fact]
    public async Task InventoryLinksToDedicatedCreationWithoutLoadingOrRenderingTheForm()
    {
        await using var factory = Factory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync("/Realms");
        Assert.Contains("href=\"/CreateRealm\">Créer un royaume", WebUtility.HtmlDecode(html));
        Assert.Contains("class=\"realm-inventory\"", html);
        Assert.Contains("href=\"/Realms/terres-aube\"", html);
        Assert.DoesNotContain("realms-workspace", html);
        Assert.DoesNotContain("Input.RealmId", html);
        Assert.DoesNotContain("Input.Islands", html);
        Assert.DoesNotContain("handler=Create", html);
        Assert.Equal("/admin/realms", Assert.Single(factory.Realms.Calls).Path);
        AdminFactory.NoSecrets(html);
    }

    [Fact]
    public async Task DedicatedCreationKeepsChoicesTopologyAndRoutesBackToInventory()
    {
        await using var factory = Factory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync("/CreateRealm");
        var decoded = WebUtility.HtmlDecode(html);
        Assert.Contains("<h1>Créer un royaume</h1>", decoded);
        Assert.Contains("action=\"/CreateRealm?handler=Create\"", html);
        Assert.Contains("formaction=\"/CreateRealm?handler=AddIsland\"", html);
        Assert.Contains("formaction=\"/CreateRealm?handler=RemoveIsland\"", html);
        Assert.Contains("href=\"/Realms\">Retour à la liste", decoded);
        Assert.Contains("href=\"/Realms\">Annuler", decoded);
        Assert.Contains("Input.TargetId", html);
        Assert.Contains("Input.ProfileId", html);
        Assert.Contains("Input.ConfigureTopology", html);
        Assert.Contains("Input.InstanceDedicatedCount", html);
        Assert.Contains("Input.RaidDedicatedCount", html);
        Assert.Equal("starter-island", AdminFactory.Input(html, "Input.Islands[0].IslandId"));
        Assert.Equal("/admin/realms/options", Assert.Single(factory.Realms.Calls).Path);
        AdminFactory.NoSecrets(html);
    }

    [Fact]
    public async Task NewCreationRouteUsesLoginAndNeverResolvesAsARealmId()
    {
        await using var factory = Factory();
        using var browser = factory.Browser();
        using var anonymous = await browser.GetAsync("/CreateRealm");
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.Contains("/Login?ReturnUrl=", anonymous.Headers.Location!.OriginalString);
        Assert.Contains("CreateRealm", anonymous.Headers.Location.OriginalString);
        Assert.Empty(factory.Realms.Calls);
        await AdminFactory.Login(browser);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/CreateRealm")).StatusCode);
        Assert.DoesNotContain(factory.Realms.Calls, call => call.Path == "/admin/realms/CreateRealm");
    }

    [Fact]
    public async Task DisabledCreationShowsItsStateWithoutCallingTheService()
    {
        await using var factory = new AdminFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync("/CreateRealm");
        Assert.Contains("La gestion des realms est désactivée", WebUtility.HtmlDecode(html));
        Assert.DoesNotContain("handler=Create", html);
        Assert.Empty(factory.Realms.Calls);
    }

    [Fact]
    public async Task StaleCreationPostsToTheInventoryCannotCreateARealm()
    {
        await using var factory = Factory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var token = await AdminFactory.Token(browser, "/Realms");
        using var response = await browser.PostAsync("/Realms?handler=Create", AdminFactory.Form(new()
        {
            ["__RequestVerificationToken"] = token, ["Input.RealmId"] = "terres-aube",
            ["Input.DisplayName"] = "Les Terres d’Aube", ["Input.TargetId"] = "test-cluster", ["Input.ProfileId"] = "island-test"
        }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(factory.Realms.Calls, call => call.Method != HttpMethod.Get);
    }

    [Fact]
    public async Task ExistingDraftEditLinksRedirectToTheDedicatedPage()
    {
        await using var factory = Factory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        using var oldLink = await browser.GetAsync("/Realms?editRealmId=terres-aube");
        Assert.Equal(HttpStatusCode.Redirect, oldLink.StatusCode);
        Assert.Equal("/CreateRealm?editRealmId=terres-aube", oldLink.Headers.Location!.OriginalString);
        Assert.Empty(factory.Realms.Calls);
        var html = await browser.GetStringAsync(oldLink.Headers.Location);
        Assert.Equal("terres-aube", AdminFactory.Input(html, "EditRealmId"));
        Assert.Contains("action=\"/CreateRealm?handler=SaveTopology\"", html);
    }

    [Fact]
    public async Task MissingChoicesDisableCreationAndKeepTheBackLink()
    {
        await using var factory = Factory();
        factory.Realms.Options = factory.Realms.Options with { Targets = [], Profiles = [] };
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = WebUtility.HtmlDecode(await browser.GetStringAsync("/CreateRealm"));
        Assert.Matches("<button[^>]+disabled=\"disabled\"[^>]*>Créer le royaume</button>", html);
        Assert.Contains("Configuration incomplète", html);
        Assert.Contains("href=\"/Realms\">Annuler", html);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedChoicesShowSafeRecoveryOnTheCreationPage(bool invalidJson)
    {
        await using var factory = Factory();
        factory.Realms.InvalidJson = invalidJson;
        if (!invalidJson) factory.Realms.ReadError = HttpStatusCode.ServiceUnavailable;
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync("/CreateRealm");
        Assert.Contains("role=\"alert\"", html);
        Assert.Contains("href=\"/CreateRealm\">Réessayer", WebUtility.HtmlDecode(html));
        Assert.DoesNotContain("handler=Create", html);
        AdminFactory.NoSecrets(html);
    }
}
