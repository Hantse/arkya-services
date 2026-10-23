using System.Net;
using Arkya.Admin.Realms;

namespace Arkya.Admin.Tests;

public sealed class RealmOperationsEndpointTests
{
    private const string Path = "/Realms/terres-aube";

    private static AdminFactory Factory()
    {
        var factory = new AdminFactory();
        factory.Settings["RealmsApi:Enabled"] = "true";
        factory.Realms.Realm = factory.Realms.Realm with { Status = "Ready", Revision = 4 };
        return factory;
    }

    [Theory]
    [InlineData("Restart")]
    [InlineData("Update")]
    [InlineData("Delete")]
    [InlineData("Announce")]
    [InlineData("Command")]
    public async Task OperationsRequireAuthenticationAndAntiforgery(string handler)
    {
        await using var factory = Factory();
        using var browser = factory.Browser();
        using var anonymous = await browser.PostAsync(Path + "?handler=" + handler, AdminFactory.Form(new()));
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.Empty(factory.Realms.Calls);
        await AdminFactory.Login(browser);
        using var unprotected = await browser.PostAsync(Path + "?handler=" + handler, AdminFactory.Form(new()));
        Assert.Equal(HttpStatusCode.BadRequest, unprotected.StatusCode);
        Assert.Empty(factory.Realms.Calls);
    }

    [Theory]
    [InlineData("Restart", "/restart")]
    [InlineData("Update", "/update")]
    public async Task LifecycleUsesDisplayedRevisionAndServerDerivedActor(string handler, string suffix)
    {
        await using var factory = Factory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = await Form(browser);
        form["Actor"] = "forged-operator";
        form["Reason"] = "  Maintenance annoncée  ";
        using var response = await browser.PostAsync(Path + "?handler=" + handler, AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(Path + "?section=maintenance", response.Headers.Location!.OriginalString);
        var call = Assert.Single(factory.Realms.Calls, call => call.Method == HttpMethod.Post);
        Assert.Equal("/admin/realms/terres-aube" + suffix, call.Path);
        Assert.Equal(4, call.Body!.Value.GetProperty("expectedRevision").GetInt64());
        Assert.Equal("Maintenance annoncée", call.Body.Value.GetProperty("reason").GetString());
        Assert.Equal("admin:operator", factory.Realms.Actors[factory.Realms.Calls.IndexOf(call)]);
        Assert.False(call.Body.Value.TryGetProperty("actor", out _));
        if (handler == "Update") Assert.Equal(factory.Realms.OperationsOptions.UpdateImage, call.Body.Value.GetProperty("expectedImage").GetString());
    }

    [Theory]
    [InlineData("Reason", "")]
    [InlineData("Reason", "bad\nreason")]
    [InlineData("ConfirmRealmId", "another-realm")]
    [InlineData("ConfirmRealmId", "")]
    public async Task DisruptionRequiresAReasonAndExactRealmConfirmation(string field, string value)
    {
        await using var factory = Factory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = await Form(browser);
        form[field] = value;
        using var response = await browser.PostAsync(Path + "?handler=Restart", AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("validation-summary-errors", await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain(factory.Realms.Calls, call => call.Method == HttpMethod.Post);
    }

    [Theory]
    [InlineData("Restart")]
    [InlineData("Update")]
    [InlineData("Delete")]
    [InlineData("Announce")]
    [InlineData("Command")]
    public async Task MissingRevisionCannotMutate(string handler)
    {
        await using var factory = Factory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = await Form(browser, handler is "Restart" or "Update" or "Delete" ? "maintenance" : "game");
        form.Remove("ExpectedRevision");
        using var response = await browser.PostAsync(Path + "?handler=" + handler, AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(factory.Realms.Calls, call => call.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task UpdateRequiresTheReviewedImageAndConflictReloadsCurrentRevision()
    {
        await using var factory = Factory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = await Form(browser);
        form.Remove("ExpectedImage");
        using var missing = await browser.PostAsync(Path + "?handler=Update", AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.OK, missing.StatusCode);
        Assert.DoesNotContain(factory.Realms.Calls, call => call.Method == HttpMethod.Post);
        form["ExpectedImage"] = factory.Realms.OperationsOptions.UpdateImage!;
        factory.Realms.WriteError = HttpStatusCode.Conflict;
        factory.Realms.Realm = factory.Realms.Realm with { Revision = 8 };
        using var conflict = await browser.PostAsync(Path + "?handler=Update", AdminFactory.Form(form));
        var html = await conflict.Content.ReadAsStringAsync();
        Assert.Equal("8", AdminFactory.Input(html, "ExpectedRevision"));
        Assert.Contains("a changé", WebUtility.HtmlDecode(html));
        AdminFactory.NoSecrets(html);
    }

    [Fact]
    public async Task AnnouncementIsQueuedWithIdempotencyAndNeverClaimsItWasDisplayed()
    {
        await using var factory = Factory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = await Form(browser, "game");
        form["Message"] = "  Maintenance dans cinq minutes.  ";
        form["CommandType"] = "shell";
        using var response = await browser.PostAsync(Path + "?handler=Announce", AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var call = Assert.Single(factory.Realms.Calls, call => call.Method == HttpMethod.Post);
        Assert.Equal("/admin/realms/terres-aube/commands", call.Path);
        var body = call.Body!.Value;
        Assert.Equal(form["RequestId"], body.GetProperty("requestId").GetString());
        Assert.Equal("announce", body.GetProperty("type").GetString());
        Assert.Equal("Maintenance dans cinq minutes.", body.GetProperty("message").GetString());
        Assert.Equal("", body.GetProperty("characterId").GetString());
        Assert.Equal("admin:operator", factory.Realms.Actors[factory.Realms.Calls.IndexOf(call)]);
        Assert.Equal(Path + "?section=game", response.Headers.Location!.OriginalString);
        var notice = WebUtility.HtmlDecode(await browser.GetStringAsync(response.Headers.Location));
        Assert.Contains("en attente du serveur", notice);
        var html = WebUtility.HtmlDecode(await browser.GetStringAsync(Path + "?section=history"));
        Assert.Contains("En attente du serveur", html);
        Assert.DoesNotContain(">Exécutée<", html);
        AdminFactory.NoSecrets(html);
    }

    [Theory]
    [InlineData("kick")]
    [InlineData("save")]
    public async Task AllowedCommandsSendOnlyTheirExpectedArguments(string type)
    {
        await using var factory = Factory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = await Form(browser, "game");
        form["CommandType"] = type;
        using var response = await browser.PostAsync(Path + "?handler=Command", AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(Path + "?section=game", response.Headers.Location!.OriginalString);
        var body = Assert.Single(factory.Realms.Calls, call => call.Method == HttpMethod.Post).Body!.Value;
        Assert.Equal(type, body.GetProperty("type").GetString());
        Assert.Equal("", body.GetProperty("message").GetString());
        Assert.Equal(type == "kick" ? form["CharacterId"] : "", body.GetProperty("characterId").GetString());
    }

    [Theory]
    [InlineData("Announce", "Message", "")]
    [InlineData("Announce", "Message", "ligne\nsuivante")]
    [InlineData("Command", "CommandType", "exec rm -rf")]
    [InlineData("Command", "CharacterId", "00000000-0000-0000-0000-000000000000")]
    [InlineData("Command", "CharacterId", "not-a-character")]
    [InlineData("Command", "ServerId", "unknown-server")]
    [InlineData("Command", "Reason", "")]
    public async Task InvalidCommandCannotReachTheWriteEndpoint(string handler, string field, string value)
    {
        await using var factory = Factory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = await Form(browser, "game");
        form[field] = value;
        using var response = await browser.PostAsync(Path + "?handler=" + handler, AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("validation-summary-errors", await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain(factory.Realms.Calls, call => call.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task AnnouncementLimitCountsUtf8Bytes()
    {
        await using var factory = Factory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = await Form(browser, "game");
        form["Message"] = new string('é', 201);
        using var response = await browser.PostAsync(Path + "?handler=Announce", AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(factory.Realms.Calls, call => call.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task DisconnectedRuntimeDisablesFormsAndRejectsHandcraftedCommands()
    {
        await using var factory = Factory();
        factory.Realms.Commands = new([new("area-one", null, false, [])], []);
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync(Path + "?section=game");
        Assert.Contains("Aucun serveur compatible", WebUtility.HtmlDecode(html));
        Assert.Contains("disabled=\"disabled\">Envoyer l’annonce", html);
        var form = await Form(browser, "game");
        using var response = await browser.PostAsync(Path + "?handler=Announce", AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(factory.Realms.Calls, call => call.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task HistoriesEscapePlayerTextAndDistinguishUnknownResults()
    {
        await using var factory = Factory();
        var now = DateTimeOffset.UtcNow;
        factory.Realms.Commands = factory.Realms.Commands with { Commands = [new(Guid.NewGuid().ToString(), "area-one", "announce",
            "<script>alert(1)</script>", "", "Unknown", "admin:operator", "Audit", now, now.AddMinutes(2), null, "private upstream details", null)] };
        factory.Realms.Realm = factory.Realms.Realm with { Operations = [new("Restart", 4, "admin:operator", "<img src=x>", now,
            now, "Failed", "private upstream details", "image-a", "image-a")] };
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync(Path + "?section=history");
        Assert.DoesNotContain("<script>alert", html);
        Assert.DoesNotContain("<img src=x>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("Résultat inconnu", WebUtility.HtmlDecode(html));
        Assert.Contains("Vérifiez en jeu avant de relancer", WebUtility.HtmlDecode(html));
        AdminFactory.NoSecrets(html);
    }

    private static async Task<Dictionary<string, string>> Form(HttpClient browser, string section = "maintenance")
    {
        var html = await browser.GetStringAsync(Path + "?section=" + section);
        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = AdminFactory.Input(html, "__RequestVerificationToken"),
            ["ExpectedRevision"] = AdminFactory.Input(html, "ExpectedRevision"),
            ["Reason"] = "Maintenance planifiée", ["ConfirmRealmId"] = "terres-aube",
            ["ServerId"] = "area-one", ["CommandType"] = "kick", ["Message"] = "Bonjour à tous.",
            ["CharacterId"] = "a8c8d448-9719-4404-a0ac-ef154801ce8e"
        };
        if (section == "maintenance") form["ExpectedImage"] = AdminFactory.Input(html, "ExpectedImage");
        else form["RequestId"] = AdminFactory.Input(html, "RequestId");
        return form;
    }
}
