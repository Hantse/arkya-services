using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arkya.Admin.Content;

namespace Arkya.Admin.Tests;

public sealed class AdminEndpointTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/?handler=Download&version=95f1b50c-85ba-49a6-9b75-e12745472cf4")]
    public async Task EditorAndDownloadRequireTheAdminCookie(string route)
    {
        await using var factory = new AdminFactory();
        using var browser = factory.Browser();
        using var response = await browser.GetAsync(route);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("https://localhost/Login?ReturnUrl=", response.Headers.Location!.OriginalString);
        Assert.Empty(factory.Content.Calls);
    }

    [Fact]
    public async Task LoginRequiresAntiforgeryAndNeverEchoesSubmittedOrConfiguredSecrets()
    {
        await using var factory = new AdminFactory();
        using var browser = factory.Browser();
        using var missingToken = await browser.PostAsync("/Login", AdminFactory.Form(new() { ["Password"] = AdminFactory.Password }));
        Assert.Equal(HttpStatusCode.BadRequest, missingToken.StatusCode);
        var token = await AdminFactory.Token(browser, "/Login");
        using var rejected = await browser.PostAsync("/Login", AdminFactory.Form(new()
        {
            ["Password"] = "incorrect-password", ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        var html = await rejected.Content.ReadAsStringAsync();
        Assert.Contains("Mot de passe incorrect", WebUtility.HtmlDecode(html));
        Assert.DoesNotContain("incorrect-password", html);
        AdminFactory.NoSecrets(html);
        Assert.Empty(factory.Content.Calls);
    }

    [Fact]
    public async Task LoginIssuesSecureHttpOnlyStrictCookieAndOpensTheEditor()
    {
        await using var factory = new AdminFactory();
        using var browser = factory.Browser();
        var token = await AdminFactory.Token(browser, "/Login");
        using var login = await browser.PostAsync("/Login", AdminFactory.Form(new()
        {
            ["Password"] = AdminFactory.Password, ["__RequestVerificationToken"] = token
        }));
        var cookie = login.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("Arkya.Admin="));
        Assert.Contains("secure", cookie);
        Assert.Contains("httponly", cookie);
        Assert.Contains("samesite=strict", cookie);
        AdminFactory.NoSecrets(cookie);
        using var index = await browser.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, index.StatusCode);
        var html = await index.Content.ReadAsStringAsync();
        Assert.Contains("La menace du bois", html);
        Assert.Equal("3", AdminFactory.Input(html, "Quests[0].RequiredKills"));
        Assert.Contains("script-src 'none'", index.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Contains("frame-ancestors 'none'", index.Headers.GetValues("Content-Security-Policy").Single());
        AdminFactory.NoSecrets(html);
        Assert.Equal(2, factory.Content.Calls.Count);
    }

    [Theory]
    [InlineData("https://attacker.example/")]
    [InlineData("//attacker.example/")]
    [InlineData("/\\attacker.example/")]
    [InlineData("javascript:alert(1)")]
    public async Task LoginCannotRedirectToAnExternalReturnUrl(string returnUrl)
    {
        await using var factory = new AdminFactory();
        using var browser = factory.Browser();
        var token = await AdminFactory.Token(browser, "/Login?ReturnUrl=" + Uri.EscapeDataString(returnUrl));
        using var response = await browser.PostAsync("/Login", AdminFactory.Form(new()
        {
            ["Password"] = AdminFactory.Password, ["__RequestVerificationToken"] = token, ["ReturnUrl"] = returnUrl
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task FailedLoginAttemptsAreRateLimited()
    {
        await using var factory = new AdminFactory();
        using var browser = factory.Browser();
        var token = await AdminFactory.Token(browser, "/Login");
        for (var attempt = 0; attempt < 9; attempt++)
        {
            using var response = await browser.PostAsync("/Login", AdminFactory.Form(new()
            {
                ["Password"] = "incorrect-password", ["__RequestVerificationToken"] = token
            }));
            Assert.Equal(attempt < 8 ? HttpStatusCode.OK : HttpStatusCode.TooManyRequests, response.StatusCode);
        }
    }

    [Theory]
    [InlineData("Save")]
    [InlineData("Publish")]
    [InlineData("Add")]
    [InlineData("Remove")]
    public async Task EveryMutationRequiresAntiforgery(string handler)
    {
        await using var factory = new AdminFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        using var response = await browser.PostAsync("/?handler=" + handler, AdminFactory.Form(new() { ["Revision"] = "3" }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.Content.Calls);
    }

    [Fact]
    public async Task SaveSendsBoundedQuestDataAndRevisionToContentUsingOnlyTheServerKey()
    {
        await using var factory = new AdminFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var token = await AdminFactory.Token(browser);
        using var saved = await browser.PostAsync("/?handler=Save", AdminFactory.Form(AdminFactory.QuestForm(token)));
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        var call = Assert.Single(factory.Content.Calls, call => call.Method == HttpMethod.Put);
        var body = call.Body!.Value.Deserialize<ContentDraft>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(3, body.Revision);
        Assert.Equal(new QuestDefinition("new-adventure", "Une nouvelle aventure", "Une description conservée.", "prototype-monster", 8), Assert.Single(body.Quests));
        using var index = await browser.GetAsync("/");
        var html = await index.Content.ReadAsStringAsync();
        Assert.Contains("Brouillon enregistré", WebUtility.HtmlDecode(html));
        Assert.Equal("4", AdminFactory.Input(html, "Revision"));
        AdminFactory.NoSecrets(html);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("10001")]
    [InlineData("invalid-number")]
    public async Task InvalidSaveKeepsUserInputAndDoesNotCallTheWriteApi(string kills)
    {
        await using var factory = new AdminFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var token = await AdminFactory.Token(browser);
        using var response = await browser.PostAsync("/?handler=Save", AdminFactory.Form(AdminFactory.QuestForm(token, "Titre conservé", kills)));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal("Titre conservé", AdminFactory.Input(html, "Quests[0].Title"));
        Assert.Equal(kills, AdminFactory.Input(html, "Quests[0].RequiredKills"));
        Assert.Contains("validation-summary-errors", html);
        Assert.DoesNotContain(factory.Content.Calls, call => call.Method != HttpMethod.Get);
        AdminFactory.NoSecrets(html);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "Vérifiez les champs")]
    [InlineData(HttpStatusCode.Conflict, "Ce brouillon a changé")]
    [InlineData(HttpStatusCode.Forbidden, "L'accès au catalogue a été refusé")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "Le catalogue est indisponible")]
    public async Task RejectedSaveShowsSafeActionableErrorAndRetainsTheSubmittedDraft(HttpStatusCode status, string message)
    {
        await using var factory = new AdminFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var token = await AdminFactory.Token(browser);
        factory.Content.WriteError = status;
        using var response = await browser.PostAsync("/?handler=Save", AdminFactory.Form(AdminFactory.QuestForm(token, "Titre conservé", "8", "2")));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains(message, WebUtility.HtmlDecode(html));
        Assert.Equal("Titre conservé", AdminFactory.Input(html, "Quests[0].Title"));
        Assert.Equal("8", AdminFactory.Input(html, "Quests[0].RequiredKills"));
        Assert.Equal("2", AdminFactory.Input(html, "Revision"));
        AdminFactory.NoSecrets(html);
    }

    [Fact]
    public async Task PublishSendsOnlyTheSavedRevisionAndRepeatedPublicationUsesTheSamePayload()
    {
        await using var factory = new AdminFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var token = await AdminFactory.Token(browser);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var published = await browser.PostAsync("/?handler=Publish", AdminFactory.Form(new()
            {
                ["__RequestVerificationToken"] = token, ["Revision"] = "3",
                ["Quests[0].Title"] = "Unsaved form must not be published", ["Quests[0].RequiredKills"] = "-10"
            }));
            Assert.Equal(HttpStatusCode.Redirect, published.StatusCode);
        }
        var calls = factory.Content.Calls.Where(call => call.Method == HttpMethod.Post).ToArray();
        Assert.Equal(2, calls.Length);
        Assert.All(calls, call =>
        {
            Assert.Equal("/admin/content/catalogs", call.Path);
            Assert.Equal("draftRevision", Assert.Single(call.Body!.Value.EnumerateObject()).Name);
            Assert.Equal(3, call.Body!.Value.GetProperty("draftRevision").GetInt64());
        });
        Assert.DoesNotContain(factory.Content.Calls, call => call.Method == HttpMethod.Put);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("-1")]
    [InlineData("not-a-revision")]
    [InlineData("9223372036854775808")]
    public async Task InvalidPublicationRevisionReturns400WithoutPublishing(string? revision)
    {
        await using var factory = new AdminFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var token = await AdminFactory.Token(browser);
        var form = new Dictionary<string, string>()
        {
            ["__RequestVerificationToken"] = token
        };
        if (revision is not null) form["Revision"] = revision;
        using var response = await browser.PostAsync("/?handler=Publish", AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(factory.Content.Calls, call => call.Method != HttpMethod.Get);
    }

    [Fact]
    public async Task AddAndRemovePreserveOtherRowsAndReindexTheEditorWithoutSaving()
    {
        await using var factory = new AdminFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var token = await AdminFactory.Token(browser);
        using var added = await browser.PostAsync("/?handler=Add", AdminFactory.Form(AdminFactory.QuestForm(token, "Une saisie conservée")));
        var html = await added.Content.ReadAsStringAsync();
        Assert.Equal("Une saisie conservée", AdminFactory.Input(html, "Quests[0].Title"));
        Assert.Equal("3", AdminFactory.Input(html, "Quests[1].RequiredKills"));
        var form = AdminFactory.QuestForm(token);
        form["Quests[1].QuestId"] = "second-quest";
        form["Quests[1].Title"] = "Seconde aventure";
        form["Quests[1].Description"] = "Seconde description";
        form["Quests[1].RequiredKills"] = "9";
        form["index"] = "0";
        using var removed = await browser.PostAsync("/?handler=Remove", AdminFactory.Form(form));
        html = await removed.Content.ReadAsStringAsync();
        Assert.Equal("Seconde aventure", AdminFactory.Input(html, "Quests[0].Title"));
        Assert.Equal("9", AdminFactory.Input(html, "Quests[0].RequiredKills"));
        Assert.DoesNotContain("name=\"Quests[1].Title\"", html);
        Assert.DoesNotContain(factory.Content.Calls, call => call.Method != HttpMethod.Get);
    }

    [Fact]
    public async Task DownloadReturnsOnlyTheRequestedImmutableCatalogAndUnknownVersionIs404()
    {
        await using var factory = new AdminFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        using var response = await browser.GetAsync("/?handler=Download&version=" + ContentStub.Version);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains($"arkya-content-{ContentStub.Version}.json", response.Content.Headers.ContentDisposition!.ToString());
        var json = await response.Content.ReadAsStringAsync();
        var catalog = JsonSerializer.Deserialize<ContentCatalog>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(ContentStub.Version, catalog.Version);
        Assert.Equal(3, Assert.Single(catalog.Quests).RequiredKills);
        AdminFactory.NoSecrets(json);
        Assert.Equal(HttpStatusCode.NotFound,
            (await browser.GetAsync("/?handler=Download&version=" + Guid.NewGuid())).StatusCode);
    }

    [Fact]
    public async Task LogoutRequiresAnAuthenticatedPostAndAntiforgery()
    {
        await using var factory = new AdminFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var token = await AdminFactory.Token(browser);
        Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync("/Logout")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostAsync("/Logout", AdminFactory.Form(new()))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/")).StatusCode);
        using var logout = await browser.PostAsync("/Logout", AdminFactory.Form(new() { ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.Equal("/Login", logout.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync("/")).StatusCode);
    }
}
