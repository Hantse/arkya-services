using System.Net;
using Arkya.Admin.Realms;

namespace Arkya.Admin.Tests;

public sealed class RealmConfigurationEndpointTests
{
    private const string Path = "/Realms/terres-aube";
    private const string SettingsPath = Path + "?section=settings";
    private const string MutationPath = Path + "?handler=Configuration";

    private static AdminFactory Factory()
    {
        var factory = new AdminFactory();
        factory.Settings["RealmsApi:Enabled"] = "true";
        factory.Realms.Realm = factory.Realms.Realm with { Status = "Ready", Revision = 42 };
        return factory;
    }

    [Fact]
    public async Task ConfigurationMutationRequiresAuthenticationAndAntiforgery()
    {
        await using var factory = Factory();
        using var browser = factory.Browser();
        Assert.Equal(HttpStatusCode.Redirect, (await browser.PostAsync(MutationPath, AdminFactory.Form(new()))).StatusCode);
        Assert.Empty(factory.Realms.Calls);
        await AdminFactory.Login(browser);
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostAsync(MutationPath, AdminFactory.Form(new()))).StatusCode);
        Assert.Empty(factory.Realms.Calls);
    }

    [Theory]
    [InlineData("0", "100")]
    [InlineData("1.125", "2.5")]
    [InlineData("1", "1")]
    public async Task ConfigurationUsesItsOwnRevisionAndAuthenticatedActorWithoutRestart(string xp, string harvest)
    {
        await using var factory = Factory();
        factory.Realms.Configuration = new(3, 1, 1, null, null, []);
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = await Form(browser);
        form["XpMultiplier"] = xp;
        form["HarvestMultiplier"] = harvest;
        form["Reason"] = "  Événement du week-end  ";
        form["Actor"] = "forged";
        using var response = await browser.PostAsync(MutationPath, AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(SettingsPath, response.Headers.Location!.OriginalString);
        var call = Assert.Single(factory.Realms.Calls, call => call.Method != HttpMethod.Get);
        Assert.Equal(HttpMethod.Put, call.Method);
        Assert.Equal("/admin/realms/terres-aube/configuration", call.Path);
        Assert.Equal(3, call.Body!.Value.GetProperty("expectedRevision").GetInt64());
        Assert.Equal(decimal.Parse(xp, System.Globalization.CultureInfo.InvariantCulture), call.Body.Value.GetProperty("xpMultiplier").GetDecimal());
        Assert.Equal(decimal.Parse(harvest, System.Globalization.CultureInfo.InvariantCulture), call.Body.Value.GetProperty("harvestMultiplier").GetDecimal());
        Assert.Equal("Événement du week-end", call.Body.Value.GetProperty("reason").GetString());
        Assert.Equal(4, call.Body.Value.EnumerateObject().Count());
        Assert.Equal("admin:operator", factory.Realms.Actors[factory.Realms.Calls.IndexOf(call)]);
        Assert.Equal(42, factory.Realms.Realm.Revision);
        var html = WebUtility.HtmlDecode(await browser.GetStringAsync(SettingsPath));
        Assert.Contains("Configuration enregistrée", html);
        Assert.Contains("vérifier sa réception par chaque serveur", html);
        AdminFactory.NoSecrets(html);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("-1")]
    [InlineData("wrong")]
    [InlineData("9223372036854775808")]
    public async Task MissingOrInvalidConfigurationRevisionCannotWrite(string? revision)
    {
        await using var factory = Factory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = await Form(browser);
        if (revision is null) form.Remove("ConfigurationRevision");
        else form["ConfigurationRevision"] = revision;
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostAsync(MutationPath, AdminFactory.Form(form))).StatusCode);
        Assert.DoesNotContain(factory.Realms.Calls, call => call.Method != HttpMethod.Get);
    }

    [Theory]
    [InlineData("XpMultiplier", "")]
    [InlineData("XpMultiplier", "-0.1")]
    [InlineData("XpMultiplier", "100.001")]
    [InlineData("XpMultiplier", "0.0001")]
    [InlineData("XpMultiplier", "1,5")]
    [InlineData("XpMultiplier", "NaN")]
    [InlineData("XpMultiplier", "1e1")]
    [InlineData("HarvestMultiplier", "-1")]
    [InlineData("HarvestMultiplier", "1000")]
    [InlineData("HarvestMultiplier", "1.2345")]
    [InlineData("Reason", "")]
    [InlineData("Reason", "invalid\nreason")]
    public async Task InvalidRatesAndReasonsShowValidationWithoutWriting(string key, string value)
    {
        await using var factory = Factory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = await Form(browser);
        form[key] = value;
        using var response = await browser.PostAsync(MutationPath, AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("validation-summary-errors", await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain(factory.Realms.Calls, call => call.Method != HttpMethod.Get);
    }

    [Fact]
    public async Task ConflictReloadsTheCurrentRatesAndTheirIndependentRevision()
    {
        await using var factory = Factory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = await Form(browser);
        factory.Realms.Configuration = new(7, 3, 4.125m, DateTimeOffset.UtcNow, "other:operator", []);
        using var response = await browser.PostAsync(MutationPath, AdminFactory.Form(form));
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("La configuration a changé", WebUtility.HtmlDecode(html));
        Assert.Equal("7", AdminFactory.Input(html, "ConfigurationRevision"));
        Assert.Equal("3", AdminFactory.Input(html, "XpMultiplier"));
        Assert.Equal("4.125", AdminFactory.Input(html, "HarvestMultiplier"));
        Assert.DoesNotContain("name=\"ExpectedRevision\"", html);
        Assert.Equal(42, factory.Realms.Realm.Revision);
        AdminFactory.NoSecrets(html);
    }

    [Theory]
    [InlineData(true, 1, 2L, "applied")]
    [InlineData(true, 1, 1L, "pending")]
    [InlineData(true, 1, null, "pending")]
    [InlineData(true, 0, 2L, "unsupported")]
    [InlineData(false, 1, 2L, "unavailable")]
    public async Task ApplicationRequiresCurrentVersionAcknowledgementFromARespondingCompatibleServer(bool available, int protocol, long? applied, string expected)
    {
        await using var factory = Factory();
        factory.Realms.Configuration = new(2, 1, 2, null, null, []);
        factory.Realms.Realm = factory.Realms.Realm with { Servers = [new("area-one", 7777, 1, 1, "localhost")] };
        factory.Realms.Commands = new([new("area-one", DateTimeOffset.UtcNow, available, ["announce"], protocol, applied)], []);
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync(SettingsPath);
        Assert.Contains("data-configuration-state=\"" + expected + "\"", html);
        var decoded = WebUtility.HtmlDecode(html);
        Assert.Contains(expected == "applied" ? "1 / 1 serveur(s)" : "0 / 1 serveur(s)", decoded);
        Assert.Contains("futur système de progression", decoded);
        Assert.Contains("l’île de la bêta ne propose pas encore cette activité", decoded);
    }

    [Fact]
    public async Task MissingDedicatedAndMissingAckAtRevisionZeroRemainPending()
    {
        await using var factory = Factory();
        factory.Realms.Commands = new([new("area-one", DateTimeOffset.UtcNow, true, ["announce"], 1, null)], []);
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync(SettingsPath);
        Assert.Contains("0 / 2 serveur(s)", WebUtility.HtmlDecode(html));
        Assert.DoesNotContain("data-configuration-state=\"applied\"", html);
        Assert.Equal("1", AdminFactory.Input(html, "XpMultiplier"));
        Assert.Equal("1", AdminFactory.Input(html, "HarvestMultiplier"));
    }

    [Fact]
    public async Task ConfigurationHistoryEscapesOperatorContent()
    {
        await using var factory = Factory();
        var now = DateTimeOffset.UtcNow;
        factory.Realms.Configuration = new(1, 2, 0, now, "<img src=x>", [new(1, 0, 2, 0, 1, 1, now, "<img src=x>", "<script>unsafe()</script>")]);
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync(Path + "?section=history");
        Assert.DoesNotContain("<script>unsafe", html);
        Assert.DoesNotContain("<img src=x>", html);
        Assert.Contains("&lt;script&gt;", html);
        AdminFactory.NoSecrets(html);
    }

    [Fact]
    public async Task ConfigurationReadFailureLeavesOtherRealmActionsAvailableWithoutLeakingDetails()
    {
        await using var factory = Factory();
        factory.Realms.ConfigurationReadError = HttpStatusCode.ServiceUnavailable;
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync(SettingsPath);
        Assert.Contains("La configuration en jeu est indisponible", WebUtility.HtmlDecode(html));
        Assert.Contains("section=maintenance", html);
        Assert.DoesNotContain("handler=Configuration", html);
        AdminFactory.NoSecrets(html);
        var maintenance = await browser.GetStringAsync(Path + "?section=maintenance");
        Assert.Contains("handler=Restart", maintenance);
        Assert.DoesNotContain("La configuration en jeu est indisponible", WebUtility.HtmlDecode(maintenance));
        AdminFactory.NoSecrets(maintenance);
    }

    [Fact]
    public async Task UnavailableCommandTrackingKeepsConfigurationEditableWithoutReportingFalsePendingStatus()
    {
        await using var factory = Factory();
        factory.Realms.CommandsReadError = HttpStatusCode.ServiceUnavailable;
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync(SettingsPath);
        var decoded = WebUtility.HtmlDecode(html);
        Assert.Contains("Le contrôle en jeu est indisponible", decoded);
        Assert.Contains("Suivi indisponible", decoded);
        Assert.Contains("data-configuration-state=\"unknown\"", html);
        Assert.DoesNotContain("data-configuration-state=\"pending\"", html);
        Assert.DoesNotContain("0 / 2 serveur(s)", decoded);
        Assert.DoesNotContain("Aucun contact", decoded);
        Assert.Contains("handler=Configuration", html);
        Assert.Equal("1", AdminFactory.Input(html, "XpMultiplier"));
        Assert.Equal("1", AdminFactory.Input(html, "HarvestMultiplier"));
        AdminFactory.NoSecrets(html);
    }

    private static async Task<Dictionary<string, string>> Form(HttpClient browser)
    {
        var html = await browser.GetStringAsync(SettingsPath);
        return new()
        {
            ["__RequestVerificationToken"] = AdminFactory.Input(html, "__RequestVerificationToken"),
            ["ConfigurationRevision"] = AdminFactory.Input(html, "ConfigurationRevision"),
            ["XpMultiplier"] = "2", ["HarvestMultiplier"] = "3", ["Reason"] = "Événement du week-end"
        };
    }
}
