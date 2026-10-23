using System.Net;
using System.Text.RegularExpressions;
using Arkya.Admin.Realms;

namespace Arkya.Admin.Tests;

public sealed class RealmDeletionEndpointTests
{
    private const string Path = "/Realms/terres-aube";
    private const string Central = "https://admin.arkya.online";
    private const string Dedicated = "https://terres-aube.admin.arkya.online";

    private static AdminFactory Factory(string status = "Ready", bool realmSites = false)
    {
        var factory = new AdminFactory();
        factory.Settings["RealmsApi:Enabled"] = "true";
        factory.Realms.Realm = factory.Realms.Realm with { Status = status, Revision = 4 };
        if (realmSites)
        {
            factory.EnvironmentName = "Production";
            factory.Settings["AllowedHosts"] = "admin.arkya.online;*.admin.arkya.online";
            factory.Settings["RealmSites:Enabled"] = "true";
        }
        return factory;
    }

    [Theory]
    [InlineData("Draft")]
    [InlineData("Ready")]
    [InlineData("Failed")]
    [InlineData("DeleteFailed")]
    public async Task DeletionUsesReviewedRevisionExactConfirmationAndServerDerivedActor(string status)
    {
        await using var factory = Factory(status);
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = await Form(browser);
        form["Actor"] = "forged-operator";
        form["Reason"] = "  Fin du test bêta  ";
        using var response = await browser.PostAsync(Path + "?handler=Delete", AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Realms", response.Headers.Location!.OriginalString);
        var call = Assert.Single(factory.Realms.Calls, call => call.Method == HttpMethod.Post);
        Assert.Equal("/admin/realms/terres-aube/delete", call.Path);
        Assert.Equal(4, call.Body!.Value.GetProperty("expectedRevision").GetInt64());
        Assert.Equal("terres-aube", call.Body.Value.GetProperty("confirmRealmId").GetString());
        Assert.Equal("Fin du test bêta", call.Body.Value.GetProperty("reason").GetString());
        Assert.Equal("admin:operator", factory.Realms.Actors[factory.Realms.Calls.IndexOf(call)]);
        Assert.False(call.Body.Value.TryGetProperty("actor", out _));
        var html = WebUtility.HtmlDecode(await browser.GetStringAsync(response.Headers.Location));
        Assert.Contains("Suppression du realm demandée", html);
        Assert.Contains("Les personnages et leurs sauvegardes sont conservés", html);
        Assert.Contains("Suppression en cours", html);
        AdminFactory.NoSecrets(html);
    }

    [Theory]
    [InlineData("Reason", "")]
    [InlineData("Reason", "bad\nreason")]
    [InlineData("ConfirmRealmId", "Terres-aube")]
    [InlineData("ConfirmRealmId", "terres-aube ")]
    [InlineData("ConfirmRealmId", "another-realm")]
    public async Task InvalidDeletionNeverReachesTheWriteEndpoint(string field, string value)
    {
        await using var factory = Factory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = await Form(browser);
        form[field] = value;
        using var response = await browser.PostAsync(Path + "?handler=Delete&section=history", AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("data-realm-section=\"maintenance\"", html);
        Assert.Contains("validation-summary-errors", html);
        var opened = Assert.Single(Regex.Matches(html, "<details\\b[^>]*realm-action-disclosure[^>]*>").Select(match => match.Value),
            value => Regex.IsMatch(value, @"\sopen(?:\s|=|>)"));
        Assert.Contains("realm-action-disclosure", opened);
        Assert.DoesNotContain(factory.Realms.Calls, call => call.Method != HttpMethod.Get);
        AdminFactory.NoSecrets(html);
    }

    [Fact]
    public async Task ConcurrentChangeReloadsTheCurrentRevisionBeforeRetrying()
    {
        await using var factory = Factory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = await Form(browser);
        factory.Realms.Realm = factory.Realms.Realm with { Revision = 8 };
        using var response = await browser.PostAsync(Path + "?handler=Delete", AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("a changé", WebUtility.HtmlDecode(html));
        Assert.Equal("8", AdminFactory.Input(html, "ExpectedRevision"));
        Assert.Equal("Ready", factory.Realms.Realm.Status);
        AdminFactory.NoSecrets(html);
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("Deploying")]
    [InlineData("Deleting")]
    public async Task BusyRealmDisablesDeletionAndRejectsHandcraftedSubmission(string status)
    {
        await using var factory = Factory(status);
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = WebUtility.HtmlDecode(await browser.GetStringAsync(Path + "?section=maintenance"));
        Assert.Contains("disabled=\"disabled\">Supprimer définitivement le realm", html);
        if (status == "Deleting") Assert.Contains("Suppression en cours", WebUtility.HtmlDecode(html));
        using var response = await browser.PostAsync(Path + "?handler=Delete", AdminFactory.Form(await Form(browser)));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("pendant une opération en cours", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        Assert.DoesNotContain(factory.Realms.Calls, call => call.Method != HttpMethod.Get);
    }

    [Fact]
    public async Task PendingOperationBlocksDeletionEvenIfTheDisplayedRealmIsReady()
    {
        await using var factory = Factory();
        factory.Realms.Realm = factory.Realms.Realm with
        {
            Operations = [new("Restart", 4, "admin:operator", "Maintenance", DateTimeOffset.UtcNow, null, "Pending", null, "image", "image")]
        };
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        using var response = await browser.PostAsync(Path + "?handler=Delete", AdminFactory.Form(await Form(browser)));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(factory.Realms.Calls, call => call.Method != HttpMethod.Get);
    }

    [Fact]
    public async Task DraftCanBeDeletedWhenClusterDeploymentIsDisabled()
    {
        await using var factory = Factory("Draft");
        factory.Realms.Options = factory.Realms.Options with { DeploymentEnabled = false };
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = WebUtility.HtmlDecode(await browser.GetStringAsync(Path + "?section=maintenance"));
        Assert.Contains("Supprimer définitivement le realm", html);
        Assert.DoesNotContain("disabled=\"disabled\">Supprimer définitivement le realm", html);
        using var response = await browser.PostAsync(Path + "?handler=Delete", AdminFactory.Form(await Form(browser)));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(factory.Realms.Calls, call => call.Path.EndsWith("/delete") && call.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task DeletionFailureOffersRetryAndShowsSafeRecoveryGuidance()
    {
        await using var factory = Factory("DeleteFailed");
        factory.Realms.Realm = factory.Realms.Realm with
        {
            LastError = "deletion_timeout",
            Operations = [new("Delete", 4, "admin:operator", "<script>delete-reason</script>", DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow, "DeleteFailed", "deletion_timeout", "image", "image")]
        };
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = WebUtility.HtmlDecode(await browser.GetStringAsync(Path + "?section=maintenance"));
        Assert.Contains("Suppression en échec", html);
        Assert.Contains("dans le délai prévu", html);
        Assert.Contains("Relancer la suppression", html);
        Assert.DoesNotContain("disabled=\"disabled\">Relancer la suppression", html);
        Assert.Contains("ils restent liés à ce realm", html);
        AdminFactory.NoSecrets(html);
        var history = await browser.GetStringAsync(Path + "?section=history");
        Assert.Contains(">Suppression<", history);
        Assert.Contains("&lt;script&gt;delete-reason&lt;/script&gt;", history);
        Assert.DoesNotContain("<script>delete-reason", history);
        AdminFactory.NoSecrets(history);
    }

    [Fact]
    public async Task DedicatedRealmDeletionRejectsHostOverrideAndReturnsNoticeToCentralCatalogue()
    {
        await using var factory = Factory(realmSites: true);
        using var browser = factory.Browser();
        browser.BaseAddress = new Uri(Central);
        await AdminFactory.Login(browser);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await browser.PostAsync(Dedicated + "/?handler=Delete", AdminFactory.Form(new()))).StatusCode);
        var form = await Form(browser, Dedicated + "/");
        form["realmId"] = "another-realm";
        factory.Realms.Calls.Clear();
        using var tampered = await browser.PostAsync(Dedicated + "/?handler=Delete", AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.NotFound, tampered.StatusCode);
        Assert.Empty(factory.Realms.Calls);
        form.Remove("realmId");
        using var response = await browser.PostAsync(Dedicated + "/?handler=Delete", AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(Central + "/Realms", response.Headers.Location!.AbsoluteUri);
        var tempCookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), value => value.StartsWith("Arkya.Admin.TempData.RealmSites="));
        Assert.Contains("domain=admin.arkya.online", tempCookie);
        Assert.Contains("secure", tempCookie);
        Assert.Contains("httponly", tempCookie);
        var html = WebUtility.HtmlDecode(await browser.GetStringAsync(response.Headers.Location));
        Assert.Contains("Suppression du realm demandée", html);
        Assert.Contains("Suppression en cours", html);
        AdminFactory.NoSecrets(html);
    }

    private static async Task<Dictionary<string, string>> Form(HttpClient browser, string path = Path)
    {
        var html = await browser.GetStringAsync(path + "?section=maintenance");
        return new()
        {
            ["__RequestVerificationToken"] = AdminFactory.Input(html, "__RequestVerificationToken"),
            ["ExpectedRevision"] = AdminFactory.Input(html, "ExpectedRevision"),
            ["Reason"] = "Fin du realm de test", ["ConfirmRealmId"] = "terres-aube"
        };
    }
}
