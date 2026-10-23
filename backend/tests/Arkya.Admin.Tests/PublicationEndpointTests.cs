using System.Net;
using System.Text.Json;
using Arkya.Admin.Content;

namespace Arkya.Admin.Tests;

public sealed class PublicationEndpointTests
{
    private static string Editor => $"/Publication/{ContentStub.PostId}";
    private static Dictionary<string, string> Form(string token, string revision = "3") => new()
    {
        ["__RequestVerificationToken"] = token, ["Revision"] = revision, ["Input.Kind"] = "release-note",
        ["Input.Language"] = "fr", ["Input.Title"] = "Les nouvelles aventures", ["Input.Summary"] = "En bref.",
        ["Input.Body"] = "Nouveau monde\nDeuxième paragraphe.", ["Input.GameVersion"] = "2026.10.23-beta+1"
    };

    [Theory]
    [InlineData("/Publications")]
    [InlineData("/Publication")]
    [InlineData("/Publication/a5bba1ae-6767-4a91-b294-78ccb1385daf")]
    public async Task PublicationsRequireTheExistingAdminSession(string route)
    {
        await using var factory = new AdminFactory();
        using var browser = factory.Browser();
        using var response = await browser.GetAsync(route);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Login?ReturnUrl=", response.Headers.Location!.OriginalString);
        Assert.Empty(factory.Content.Calls);
    }

    [Theory]
    [InlineData("/Publication?handler=Save")]
    [InlineData("/Publication/a5bba1ae-6767-4a91-b294-78ccb1385daf?handler=Save")]
    [InlineData("/Publication/a5bba1ae-6767-4a91-b294-78ccb1385daf?handler=Publish")]
    [InlineData("/Publication/a5bba1ae-6767-4a91-b294-78ccb1385daf?handler=Unpublish")]
    public async Task AllPublicationMutationsRequireAntiforgery(string route)
    {
        await using var factory = new AdminFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        using var response = await browser.PostAsync(route, AdminFactory.Form(new() { ["Revision"] = "3" }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.Content.Calls);
    }

    [Fact]
    public async Task ListKeepsFiltersAndPaginationAndUsesOnlyThePrivateContentKey()
    {
        await using var factory = new AdminFactory();
        factory.Content.PostsHaveMore = true;
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        using var response = await browser.GetAsync("/Publications?kind=release-note&language=en&offset=20&limit=10");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("?offset=20&limit=10&kind=release-note&language=en", Assert.Single(factory.Content.PostQueries));
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("Une île à découvrir", html);
        Assert.Contains("offset=30", html);
        Assert.Contains("offset=10", html);
        Assert.Contains("Create a draft", html);
        AdminFactory.NoSecrets(html);
    }

    [Theory]
    [InlineData("kind=other")]
    [InlineData("language=other")]
    [InlineData("offset=-1")]
    [InlineData("offset=10001")]
    [InlineData("limit=51")]
    public async Task InvalidListFilterDoesNotReachContent(string query)
    {
        await using var factory = new AdminFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        using var response = await browser.GetAsync("/Publications?" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("validation-summary-errors", await response.Content.ReadAsStringAsync());
        Assert.Empty(factory.Content.Calls);
    }

    [Theory]
    [InlineData("news")]
    [InlineData("release-note")]
    public async Task CreateSavesOnlyADraftAndLinksReleaseNotesToTheVersion(string kind)
    {
        await using var factory = new AdminFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var token = await AdminFactory.Token(browser, "/Publication");
        var form = Form(token, "0");
        form["Input.Kind"] = kind;
        form["Input.Title"] = "  Un titre  ";
        using var response = await browser.PostAsync("/Publication?handler=Save", AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(Editor, response.Headers.Location!.OriginalString);
        var call = Assert.Single(factory.Content.Calls);
        Assert.Equal(HttpMethod.Post, call.Method);
        Assert.Equal("/admin/content/posts", call.Path);
        var draft = call.Body!.Value.Deserialize<CreateContentPost>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal("Un titre", draft.Title);
        Assert.Equal(kind == "news" ? null : "2026.10.23-beta+1", draft.GameVersion);
        Assert.Null(factory.Content.Post.PublishedAt);
    }

    [Theory]
    [InlineData("Input.Kind", "invalid")]
    [InlineData("Input.Language", "invalid")]
    [InlineData("Input.GameVersion", "")]
    [InlineData("Input.GameVersion", "../unsafe")]
    [InlineData("Input.Title", " ")]
    [InlineData("Input.Title", "first\nsecond")]
    [InlineData("Input.Summary", "first\nsecond")]
    [InlineData("Input.Body", "")]
    [InlineData("Input.Body", "unsafe\u0001text")]
    public async Task InvalidEditorInputIsRetainedWithoutWriting(string field, string value)
    {
        await using var factory = new AdminFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = Form(await AdminFactory.Token(browser, Editor));
        form[field] = value;
        using var response = await browser.PostAsync(Editor + "?handler=Save", AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("validation-summary-errors", html);
        Assert.Equal("3", AdminFactory.Input(html, "Revision"));
        Assert.DoesNotContain(factory.Content.Calls, call => call.Method != HttpMethod.Get);
    }

    [Theory]
    [InlineData("Input.Title", 161)]
    [InlineData("Input.Summary", 401)]
    [InlineData("Input.Body", 20001)]
    [InlineData("Input.GameVersion", 65)]
    public async Task OversizedFieldsCannotReachTheWriteApi(string field, int length)
    {
        await using var factory = new AdminFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = Form(await AdminFactory.Token(browser, Editor));
        form[field] = new string('a', length);
        using var response = await browser.PostAsync(Editor + "?handler=Save", AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("validation-summary-errors", await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain(factory.Content.Calls, call => call.Method != HttpMethod.Get);
    }

    [Fact]
    public async Task SavingPublishedTextDoesNotPublishItAndShowsPendingChanges()
    {
        await using var factory = new AdminFactory();
        factory.Content.Post = factory.Content.Post with { PublishedAt = factory.Content.Post.UpdatedAt, PublishedRevision = 3 };
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = Form(await AdminFactory.Token(browser, Editor));
        using var saved = await browser.PostAsync(Editor + "?handler=Save", AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        var call = Assert.Single(factory.Content.Calls, call => call.Method != HttpMethod.Get);
        Assert.Equal(HttpMethod.Put, call.Method);
        Assert.Equal(3, call.Body!.Value.GetProperty("revision").GetInt64());
        Assert.Equal(3, factory.Content.Post.PublishedRevision);
        using var page = await browser.GetAsync(Editor);
        var html = WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync());
        Assert.Contains("unpublished changes", html);
        Assert.Contains("Unpublish", html);
    }

    [Theory]
    [InlineData("Publish", "publish")]
    [InlineData("Unpublish", "unpublish")]
    public async Task VisibilityUsesOnlySavedRevisionAndIgnoresUnsavedFields(string handler, string endpoint)
    {
        await using var factory = new AdminFactory();
        factory.Content.Post = factory.Content.Post with { PublishedAt = factory.Content.Post.UpdatedAt, PublishedRevision = 3 };
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var token = await AdminFactory.Token(browser, Editor);
        using var response = await browser.PostAsync(Editor + "?handler=" + handler, AdminFactory.Form(new()
        {
            ["__RequestVerificationToken"] = token, ["Revision"] = "3", ["Input.Title"] = "Unsaved", ["Input.Body"] = ""
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var call = Assert.Single(factory.Content.Calls, call => call.Method != HttpMethod.Get);
        Assert.Equal($"/admin/content/posts/{ContentStub.PostId}/{endpoint}", call.Path);
        Assert.Equal("revision", Assert.Single(call.Body!.Value.EnumerateObject()).Name);
        Assert.Equal(3, call.Body.Value.GetProperty("revision").GetInt64());
        Assert.Equal(handler == "Publish" ? 4 : (long?)null, factory.Content.Post.PublishedRevision);
    }

    [Theory]
    [InlineData("Save", null)]
    [InlineData("Save", "0")]
    [InlineData("Publish", null)]
    [InlineData("Publish", "-1")]
    [InlineData("Publish", "9223372036854775807")]
    [InlineData("Unpublish", "garbage")]
    public async Task RevisionMustBePresentAndPositiveForExistingPublications(string handler, string? revision)
    {
        await using var factory = new AdminFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = Form(await AdminFactory.Token(browser, Editor));
        if (revision is null) form.Remove("Revision"); else form["Revision"] = revision;
        using var response = await browser.PostAsync(Editor + "?handler=" + handler, AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(factory.Content.Calls, call => call.Method != HttpMethod.Get);
    }

    [Fact]
    public async Task ConflictRetainsSubmittedTextAndStaleRevisionWithoutSilentlyOverwriting()
    {
        await using var factory = new AdminFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = Form(await AdminFactory.Token(browser, Editor));
        factory.Content.Post = factory.Content.Post with { Revision = 4, Title = "Another operator's edit" };
        using var response = await browser.PostAsync(Editor + "?handler=Save", AdminFactory.Form(form));
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Copy your changes", WebUtility.HtmlDecode(html));
        Assert.Equal("Les nouvelles aventures", AdminFactory.Input(html, "Input.Title"));
        Assert.Equal("3", AdminFactory.Input(html, "Revision"));
        Assert.Equal("Another operator's edit", factory.Content.Post.Title);
    }

    [Fact]
    public async Task EditorListAndPreviewEncodeUntrustedText()
    {
        await using var factory = new AdminFactory();
        factory.Content.Post = factory.Content.Post with { Title = "<img src=x onerror=alert(1)>", Summary = "<b>Résumé</b>", Body = "</textarea><script>alert(1)</script>\nTexte" };
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        foreach (var route in new[] { Editor, "/Publications" })
        {
            using var response = await browser.GetAsync(route);
            var html = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("<img src=x", html);
            Assert.DoesNotContain("<script>", html);
            Assert.Contains("&lt;img", html);
            Assert.Contains("script-src 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
            AdminFactory.NoSecrets(html);
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task DependencyFailuresNeverExposeRawUpstreamDetails(HttpStatusCode status)
    {
        await using var factory = new AdminFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = Form(await AdminFactory.Token(browser, Editor));
        factory.Content.WriteError = status;
        using var response = await browser.PostAsync(Editor + "?handler=Save", AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AdminFactory.NoSecrets(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CompleteContentOutageRetainsTheEditorAndItsExistingDestination()
    {
        await using var factory = new AdminFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = Form(await AdminFactory.Token(browser, Editor));
        factory.Content.WriteError = HttpStatusCode.ServiceUnavailable;
        factory.Content.PostReadError = HttpStatusCode.ServiceUnavailable;
        using var response = await browser.PostAsync(Editor + "?handler=Save", AdminFactory.Form(form));
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Les nouvelles aventures", AdminFactory.Input(html, "Input.Title"));
        Assert.Equal("3", AdminFactory.Input(html, "Revision"));
        Assert.Contains($"action=\"{Editor}?handler=Save\"", html);
        Assert.DoesNotContain("Create draft", WebUtility.HtmlDecode(html));
        AdminFactory.NoSecrets(html);
    }

    [Fact]
    public async Task MissingPublicationReturnsNotFoundAndDisabledContentMakesNoCalls()
    {
        await using var factory = new AdminFactory();
        factory.Settings["ContentApi:Enabled"] = "false";
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        using var list = await browser.GetAsync("/Publications");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Contains("disabled", WebUtility.HtmlDecode(await list.Content.ReadAsStringAsync()));
        using var editor = await browser.GetAsync(Editor);
        Assert.Equal(HttpStatusCode.NotFound, editor.StatusCode);
        Assert.Empty(factory.Content.Calls);
    }

    [Fact]
    public async Task UnknownPublicationReturnsNotFound()
    {
        await using var factory = new AdminFactory();
        factory.Content.PostReadError = HttpStatusCode.NotFound;
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        using var response = await browser.GetAsync(Editor);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
