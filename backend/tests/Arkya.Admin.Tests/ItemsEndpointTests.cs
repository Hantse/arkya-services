using System.Net;
using System.Text.Json;
using Arkya.Admin.Content;

namespace Arkya.Admin.Tests;

public sealed class ItemsEndpointTests
{
    private static Dictionary<string, string> Form(string token, string revision = "3") => new()
    {
        ["__RequestVerificationToken"] = token, ["Revision"] = revision, ["Input.ItemId"] = "sword", ["Input.Name"] = "  Épée de garde  ",
        ["Input.Description"] = "Une lame peinte.", ["Input.Category"] = "weapon", ["Input.Rarity"] = "rare", ["Input.EquipmentSlot"] = "main-hand",
        ["Input.VisualAssetGuid"] = ContentStub.AssetGuid, ["Input.MaxStack"] = "1", ["Input.WeightGrams"] = "1200", ["Input.RequiredLevel"] = "2",
        ["Input.Attack"] = "12", ["Input.Defense"] = "0"
    };

    [Theory]
    [InlineData("/Items")]
    [InlineData("/Item")]
    [InlineData("/Item/sword")]
    [InlineData("/Item?handler=Assets")]
    [InlineData("/Item?handler=Thumbnail&guid=1234567890abcdef1234567890abcdef")]
    public async Task ItemsAndPreviewsRequireTheExistingAdminSession(string route)
    {
        await using var factory = new AdminFactory();
        using var browser = factory.Browser();
        using var response = await browser.GetAsync(route);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Login?ReturnUrl=", response.Headers.Location!.OriginalString);
        Assert.Empty(factory.Content.Calls);
    }

    [Theory]
    [InlineData("/Item?handler=Save")]
    [InlineData("/Item/sword?handler=Save")]
    [InlineData("/Items?handler=Publish")]
    public async Task MutationsRequireAntiforgery(string route)
    {
        await using var factory = new AdminFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        using var response = await browser.PostAsync(route, AdminFactory.Form(new() { ["Revision"] = "3" }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.Content.Calls);
    }

    [Fact]
    public async Task EditorShowsSelectableModelsAndPrivateThumbnailProxy()
    {
        await using var factory = new AdminFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        using var response = await browser.GetAsync("/Item/sword");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Assets/Arkya/Items/Sword.prefab", html);
        Assert.Contains("Assets/Arkya/Items/Shield.fbx", html);
        Assert.Contains("handler=Thumbnail", html);
        Assert.Contains("data-guid=\"" + ContentStub.AssetGuid + "\"", html);
        Assert.Contains("aria-pressed=\"true\"", html);
        Assert.Contains("script-src 'self'", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal(ContentStub.AssetGuid, AdminFactory.Input(html, "Input.VisualAssetGuid"));
        AdminFactory.NoSecrets(html);
    }

    [Fact]
    public async Task SaveMergesOnlyTheEditedItemAndDoesNotPublish()
    {
        await using var factory = new AdminFactory();
        var resource = ContentStub.Sword with { ItemId = "iron", Name = "Fer", Category = "material", EquipmentSlot = "none", MaxStack = 99 };
        factory.Content.ItemsDraft = new(3, [ContentStub.Sword, resource]);
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = Form(await AdminFactory.Token(browser, "/Item/sword"));
        using var response = await browser.PostAsync("/Item/sword?handler=Save", AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Item/sword", response.Headers.Location!.OriginalString);
        Assert.Equal(resource, factory.Content.ItemsDraft.Items.Single(i => i.ItemId == "iron"));
        Assert.Equal("Épée de garde", factory.Content.ItemsDraft.Items.Single(i => i.ItemId == "sword").Name);
        var call = Assert.Single(factory.Content.Calls, c => c.Method != HttpMethod.Get);
        Assert.Equal(HttpMethod.Put, call.Method);
        Assert.Equal("/admin/content/items/draft", call.Path);
        Assert.Equal(3, call.Body!.Value.GetProperty("revision").GetInt64());
    }

    [Fact]
    public async Task NewItemAppendsToTheSavedDraft()
    {
        await using var factory = new AdminFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = Form(await AdminFactory.Token(browser, "/Item"));
        form["Input.ItemId"] = "new-sword";
        using var response = await browser.PostAsync("/Item?handler=Save", AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Item/new-sword", response.Headers.Location!.OriginalString);
        Assert.Equal(2, factory.Content.ItemsDraft.Items.Length);
        Assert.Equal(ContentStub.Sword, factory.Content.ItemsDraft.Items[0]);
    }

    [Theory]
    [InlineData("Input.ItemId", "changed-id")]
    [InlineData("Input.Name", " ")]
    [InlineData("Input.Name", "first\nsecond")]
    [InlineData("Input.Category", "invalid")]
    [InlineData("Input.Rarity", "invalid")]
    [InlineData("Input.EquipmentSlot", "chest")]
    [InlineData("Input.VisualAssetGuid", "../unsafe")]
    [InlineData("Input.MaxStack", "2")]
    [InlineData("Input.WeightGrams", "-1")]
    [InlineData("Input.RequiredLevel", "0")]
    [InlineData("Input.Attack", "1000001")]
    public async Task InvalidFieldsRetainEditsWithoutWriting(string field, string value)
    {
        await using var factory = new AdminFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = Form(await AdminFactory.Token(browser, "/Item/sword"));
        form[field] = value;
        using var response = await browser.PostAsync("/Item/sword?handler=Save", AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("validation-summary-errors", html);
        Assert.Equal("3", AdminFactory.Input(html, "Revision"));
        Assert.DoesNotContain(factory.Content.Calls, c => c.Method != HttpMethod.Get);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("-1")]
    [InlineData("9223372036854775807")]
    [InlineData("garbage")]
    public async Task RevisionMustBePresentAndValid(string? revision)
    {
        await using var factory = new AdminFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = Form(await AdminFactory.Token(browser, "/Item/sword"));
        if (revision is null) form.Remove("Revision"); else form["Revision"] = revision;
        using var response = await browser.PostAsync("/Item/sword?handler=Save", AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(factory.Content.Calls, c => c.Method != HttpMethod.Get);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentChangesAndUpstreamConflictsRetainEditsAndStaleRevision(bool upstreamConflict)
    {
        await using var factory = new AdminFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = Form(await AdminFactory.Token(browser, "/Item/sword"));
        if (upstreamConflict) factory.Content.WriteError = HttpStatusCode.Conflict;
        else factory.Content.ItemsDraft = factory.Content.ItemsDraft with { Revision = 4 };
        using var response = await browser.PostAsync("/Item/sword?handler=Save", AdminFactory.Form(form));
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Copy your changes", WebUtility.HtmlDecode(html));
        Assert.Equal("  Épée de garde  ", AdminFactory.Input(html, "Input.Name"));
        Assert.Equal("3", AdminFactory.Input(html, "Revision"));
        Assert.Equal(ContentStub.Sword, Assert.Single(factory.Content.ItemsDraft.Items));
        AdminFactory.NoSecrets(html);
    }

    [Fact]
    public async Task PublishUsesOnlySavedRevisionAndShowsImmutableHistory()
    {
        await using var factory = new AdminFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var token = await AdminFactory.Token(browser, "/Items");
        using var response = await browser.PostAsync("/Items?handler=Publish", AdminFactory.Form(new()
        { ["__RequestVerificationToken"] = token, ["Revision"] = "3", ["Input.Name"] = "Unsaved name" }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var call = Assert.Single(factory.Content.Calls, c => c.Method != HttpMethod.Get);
        Assert.Equal("draftRevision", Assert.Single(call.Body!.Value.EnumerateObject()).Name);
        Assert.Equal(3, call.Body!.Value.GetProperty("draftRevision").GetInt64());
        using var page = await browser.GetAsync("/Items");
        var html = WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync());
        Assert.Contains(ContentStub.Version, html);
        Assert.Contains("Content pipeline", html);
        AdminFactory.NoSecrets(html);
    }

    [Fact]
    public async Task AssetSearchUsesBoundedPagingAndEscapesQueryWithoutExposingKeys()
    {
        await using var factory = new AdminFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        using var response = await browser.GetAsync("/Item?handler=Assets&search=Sword%20%26%20Shield&offset=24");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("?limit=24&offset=24&search=Sword%20%26%20Shield", Assert.Single(factory.Content.ItemAssetQueries));
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        AdminFactory.NoSecrets(await response.Content.ReadAsStringAsync());
        foreach (var query in new[] { "offset=-1", "offset=10001", "offset=bad", "search=" + new string('a', 121) })
        {
            var before = factory.Content.Calls.Count;
            using var invalid = await browser.GetAsync("/Item?handler=Assets&" + query);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Equal(before, factory.Content.Calls.Count);
        }
    }

    [Fact]
    public async Task ThumbnailIsProxiedAsPrivatePngAndRejectsInvalidGuidsAndUnsafeContent()
    {
        await using var factory = new AdminFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var route = "/Item?handler=Thumbnail&guid=" + ContentStub.AssetGuid;
        using var response = await browser.GetAsync(route);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(factory.Content.Thumbnail, await response.Content.ReadAsByteArrayAsync());
        Assert.Contains("private", response.Headers.CacheControl!.ToString());
        factory.Content.ThumbnailType = "text/html";
        using var unsafeResponse = await browser.GetAsync(route);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unsafeResponse.StatusCode);
        factory.Content.ThumbnailType = "image/png";
        factory.Content.Thumbnail = new byte[131073];
        using var oversized = await browser.GetAsync(route);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, oversized.StatusCode);
        var before = factory.Content.Calls.Count;
        using var invalid = await browser.GetAsync("/Item?handler=Thumbnail&guid=../unsafe");
        Assert.Equal(HttpStatusCode.NotFound, invalid.StatusCode);
        Assert.Equal(before, factory.Content.Calls.Count);
    }

    [Fact]
    public async Task ItemAndAssetTextIsEncodedAndCannotInjectMarkupOrScripts()
    {
        await using var factory = new AdminFactory();
        factory.Content.ItemsDraft = new(3, [ContentStub.Sword with { Name = "<script>alert(1)</script>", Description = "</textarea><script>alert(2)</script>" }]);
        factory.Content.ItemAssets = [factory.Content.ItemAssets[0] with { Name = "<img src=x onerror=alert(3)>", AssetPath = "<script>alert(4)</script>" }];
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        foreach (var route in new[] { "/Item/sword", "/Items" })
        {
            using var response = await browser.GetAsync(route);
            var html = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("<script>alert(", html);
            Assert.DoesNotContain("<img src=x", html);
            Assert.Contains("&lt;script&gt;", html);
        }
    }
}
