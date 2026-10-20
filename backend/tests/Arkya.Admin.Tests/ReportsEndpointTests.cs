using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Arkya.ServiceDefaults.Reports;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Arkya.Admin.Tests;

public sealed class ReportsEndpointTests
{
    private static string Detail => $"/BugReport?id={ReportsStub.Id:D}";
    private static string Screenshot => Detail + "&handler=Screenshot";
    private static string Save => Detail + "&handler=Save";

    private static AdminFactory EnabledFactory()
    {
        var factory = new AdminFactory();
        factory.Settings["ReportsApi:Enabled"] = "true";
        return factory;
    }

    [Fact]
    public async Task ReportsAndScreenshotsRequireAuthenticationAndUpdatesRequireAntiforgery()
    {
        await using var factory = EnabledFactory();
        using var browser = factory.Browser();
        foreach (var path in new[] { "/BugReports", Detail, Screenshot })
        {
            using var response = await browser.GetAsync(path);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.StartsWith("https://localhost/Login?", response.Headers.Location!.AbsoluteUri);
        }
        Assert.Empty(factory.Reports.Calls);
        await AdminFactory.Login(browser);
        using var rejected = await browser.PostAsync(Save, AdminFactory.Form(new() { ["ExpectedRevision"] = "1", ["Input.Status"] = "closed" }));
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Empty(factory.Reports.Calls);
    }

    [Fact]
    public async Task DisabledReportsDoNotCallTheService()
    {
        await using var factory = new AdminFactory();
        factory.Settings["ReportsApi:AdminKey"] = "";
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync("/BugReports");
        Assert.Contains("La gestion des signalements est désactivée", WebUtility.HtmlDecode(html));
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync(Detail)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync(Screenshot)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.PostAsync(Save, AdminFactory.Form(SaveForm(AdminFactory.Input(html, "__RequestVerificationToken"))))).StatusCode);
        Assert.Empty(factory.Reports.Calls);
        AdminFactory.NoSecrets(html);
    }

    [Theory]
    [InlineData(BugReportKinds.Bug)]
    [InlineData(BugReportKinds.Suggestion)]
    public async Task FiltersAndPaginationAreForwardedAndPlayerTextIsEncoded(string kind)
    {
        await using var factory = EnabledFactory();
        factory.Reports.Total = 80;
        factory.Reports.Report = factory.Reports.Report with { Title = "<script>player-input</script>", Kind = kind };
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync("/BugReports?tag=%20URGENT%20&status=triaged&offset=20&limit=20&kind=" + kind);
        var call = Assert.Single(factory.Reports.Calls);
        var query = QueryHelpers.ParseQuery(call.Uri.Query);
        Assert.Equal("urgent", query["tag"].ToString());
        Assert.Equal("triaged", query["status"].ToString());
        Assert.Equal("20", query["offset"].ToString());
        Assert.Equal("20", query["limit"].ToString());
        Assert.Equal(kind, query["kind"].ToString());
        Assert.Contains("&lt;script&gt;player-input&lt;/script&gt;", html);
        Assert.DoesNotContain("<script>player-input", html);
        Assert.Contains("offset=40", html);
        Assert.Contains("Précédents", WebUtility.HtmlDecode(html));
        var nextLink = Regex.Matches(html, "href=\"([^\"]+)\"").Select(match => WebUtility.HtmlDecode(match.Groups[1].Value))
            .Select(href => QueryHelpers.ParseQuery(new Uri(new Uri("https://localhost"), href).Query))
            .Single(parameters => parameters.TryGetValue("offset", out var offset) && offset == "40");
        Assert.Equal(kind, nextLink["kind"].ToString());
        Assert.Equal("urgent", nextLink["tag"].ToString());
        Assert.Equal("triaged", nextLink["status"].ToString());
        AdminFactory.NoSecrets(html);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?kind=")]
    public async Task DefaultListIncludesBothKinds(string query)
    {
        await using var factory = EnabledFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync("/BugReports" + query);
        var call = Assert.Single(factory.Reports.Calls);
        Assert.False(QueryHelpers.ParseQuery(call.Uri.Query).ContainsKey("kind"));
        Assert.Contains("Tous les types", html);
    }

    [Theory]
    [InlineData("?tag=../secret")]
    [InlineData("?status=unknown")]
    [InlineData("?kind=unknown")]
    [InlineData("?kind=Suggestion")]
    [InlineData("?offset=-1")]
    [InlineData("?offset=100001")]
    [InlineData("?limit=101")]
    [InlineData("?limit=0")]
    public async Task InvalidListParametersDoNotReachTheApi(string query)
    {
        await using var factory = EnabledFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        Assert.Contains("validation-summary-errors", await browser.GetStringAsync("/BugReports" + query));
        Assert.Empty(factory.Reports.Calls);
    }

    [Fact]
    public async Task EmptyListHasAUsefulState()
    {
        await using var factory = EnabledFactory();
        factory.Reports.Empty = true;
        factory.Reports.Total = 0;
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        Assert.Contains("Aucun rapport sur cette page", await browser.GetStringAsync("/BugReports"));
    }

    [Fact]
    public async Task DetailUsesProtectedScreenshotAndEncodesPlayerContent()
    {
        await using var factory = EnabledFactory();
        factory.Reports.Report = factory.Reports.Report with { Description = "<script>description</script>", Scene = "<img src=x>", Resolution = "</textarea><script>resolution</script>",
            History = [new(1, DateTimeOffset.UtcNow, "<script>actor</script>", "new", ["urgent"], "<script>history</script>")] };
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync(Detail);
        Assert.Contains("handler=Screenshot", html);
        Assert.Contains("&lt;script&gt;description&lt;/script&gt;", html);
        Assert.Contains("&lt;script&gt;history&lt;/script&gt;", html);
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("reports.example", html);
        Assert.Equal("1", AdminFactory.Input(html, "ExpectedRevision"));
        AdminFactory.NoSecrets(html);
    }

    [Fact]
    public async Task SuggestionShowsItsTypeAndRealizedStatusWithTheSameProtectedScreenshot()
    {
        await using var factory = EnabledFactory();
        factory.Reports.Report = factory.Reports.Report with
        {
            Kind = BugReportKinds.Suggestion, Title = "Ajouter un pont", Status = BugReportStatuses.Resolved,
            History = [new(2, DateTimeOffset.UtcNow, "admin:operator", BugReportStatuses.Resolved, [], "Pont ajouté et testé.")]
        };
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var list = await browser.GetStringAsync("/BugReports?kind=suggestion");
        Assert.Contains("<span class=\"badge\">Suggestion</span>", list);
        Assert.Contains("Réalisée", WebUtility.HtmlDecode(list));
        var html = WebUtility.HtmlDecode(await browser.GetStringAsync(Detail));
        Assert.Contains("<dt>Type</dt><dd>Suggestion</dd>", html);
        Assert.Contains("Réalisée", html);
        Assert.DoesNotContain("Résolu", html);
        Assert.Contains("Réponse / réalisation", html);
        Assert.Contains("handler=Screenshot", html);
        using var screenshot = await browser.GetAsync(Screenshot);
        Assert.Equal(HttpStatusCode.OK, screenshot.StatusCode);
        Assert.Equal(factory.Reports.Screenshot, await screenshot.Content.ReadAsByteArrayAsync());
        AdminFactory.NoSecrets(html);
    }

    [Fact]
    public async Task OldResponsesWithoutKindRemainVisibleAsBugs()
    {
        await using var factory = EnabledFactory();
        factory.Reports.OmitKind = true;
        factory.Reports.Report = factory.Reports.Report with { Status = BugReportStatuses.Resolved };
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        Assert.Contains("<span class=\"badge\">Bug</span>", await browser.GetStringAsync("/BugReports"));
        var html = WebUtility.HtmlDecode(await browser.GetStringAsync(Detail));
        Assert.Contains("<dt>Type</dt><dd>Bug</dd>", html);
        Assert.Contains("Résolu", html);
        Assert.DoesNotContain("Réalisée", html);
        AdminFactory.NoSecrets(html);
    }

    [Fact]
    public async Task SuggestionUpdateCannotChangeKind()
    {
        await using var factory = EnabledFactory();
        factory.Reports.Report = factory.Reports.Report with { Kind = BugReportKinds.Suggestion };
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = SaveForm(await AdminFactory.Token(browser, Detail));
        form["Input.Kind"] = BugReportKinds.Bug;
        form["Input.Status"] = BugReportStatuses.Resolved;
        using var response = await browser.PostAsync(Save, AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(BugReportKinds.Suggestion, factory.Reports.Report.Kind);
        var call = Assert.Single(factory.Reports.Calls, call => call.Method == HttpMethod.Patch);
        Assert.False(call.Body!.Value.TryGetProperty("kind", out _));
        Assert.Equal("resolved", call.Body.Value.GetProperty("status").GetString());
    }

    [Fact]
    public async Task ScreenshotReturnsOnlyPngWithPrivateCachePolicy()
    {
        await using var factory = EnabledFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        using var response = await browser.GetAsync(Screenshot);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(factory.Reports.Screenshot, await response.Content.ReadAsByteArrayAsync());
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        var call = Assert.Single(factory.Reports.Calls);
        Assert.EndsWith("/screenshot", call.Uri.AbsolutePath);
    }

    [Theory]
    [InlineData("type")]
    [InlineData("signature")]
    [InlineData("size")]
    public async Task InvalidScreenshotDoesNotReachBrowser(string failure)
    {
        await using var factory = EnabledFactory();
        if (failure == "type") factory.Reports.ScreenshotContentType = "text/html";
        if (failure == "signature") factory.Reports.Screenshot = "<script>bad image</script>"u8.ToArray();
        if (failure == "size") factory.Reports.Screenshot = new byte[2 * 1024 * 1024 + 1];
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        using var response = await browser.GetAsync(Screenshot);
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task UpdateNormalizesTagsAndSendsOnlyTriageAndExpectedRevision()
    {
        await using var factory = EnabledFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = SaveForm(await AdminFactory.Token(browser, Detail));
        form["Input.Tags"] = " Urgent, GAMEPLAY , urgent ";
        form["Input.Resolution"] = "Collision corrigée.";
        form["Input.AccountId"] = "untrusted-account";
        form["Actor"] = "forged-actor";
        using var response = await browser.PostAsync(Save, AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var call = Assert.Single(factory.Reports.Calls, call => call.Method == HttpMethod.Patch);
        Assert.Equal("admin:operator", call.Actor);
        Assert.Equal(4, call.Body!.Value.EnumerateObject().Count());
        var update = call.Body.Value.Deserialize<UpdateBugReportRequest>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(1, update.ExpectedRevision);
        Assert.Equal("in_progress", update.Status);
        Assert.Equal(["urgent", "gameplay"], update.Tags!);
        Assert.Equal("Collision corrigée.", update.Resolution);
        var html = await browser.GetStringAsync(response.Headers.Location);
        Assert.Contains("Rapport mis à jour.", WebUtility.HtmlDecode(html));
        AdminFactory.NoSecrets(html);
    }

    [Fact]
    public async Task ClearingTagsAndResolutionIsAllowed()
    {
        await using var factory = EnabledFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = SaveForm(await AdminFactory.Token(browser, Detail));
        form["Input.Tags"] = "";
        using var response = await browser.PostAsync(Save, AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var call = Assert.Single(factory.Reports.Calls, call => call.Method == HttpMethod.Patch);
        Assert.Empty(call.Body!.Value.GetProperty("tags").EnumerateArray());
        Assert.Equal("", call.Body.Value.GetProperty("resolution").GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("invalid")]
    [InlineData("9223372036854775808")]
    public async Task MissingOrInvalidRevisionCannotUpdate(string? revision)
    {
        await using var factory = EnabledFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = SaveForm(await AdminFactory.Token(browser, Detail));
        if (revision is null) form.Remove("ExpectedRevision");
        else form["ExpectedRevision"] = revision;
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostAsync(Save, AdminFactory.Form(form))).StatusCode);
        Assert.DoesNotContain(factory.Reports.Calls, call => call.Method == HttpMethod.Patch);
    }

    [Theory]
    [InlineData("Input.Status", "unknown")]
    [InlineData("Input.Tags", "../private")]
    [InlineData("Input.Tags", "a,b,c,d,e,f,g,h,i,j,k")]
    [InlineData("Input.Tags", "123456789012345678901234567890123")]
    public async Task InvalidTriageDoesNotUpdate(string field, string value)
    {
        await using var factory = EnabledFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = SaveForm(await AdminFactory.Token(browser, Detail));
        form[field] = value;
        var html = await (await browser.PostAsync(Save, AdminFactory.Form(form))).Content.ReadAsStringAsync();
        Assert.Contains("validation-summary-errors", html);
        Assert.DoesNotContain(factory.Reports.Calls, call => call.Method == HttpMethod.Patch);
    }

    [Fact]
    public async Task ConflictReloadsBothRevisionAndCurrentTriageBeforeRetry()
    {
        await using var factory = EnabledFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var token = await AdminFactory.Token(browser, Detail);
        factory.Reports.WriteError = HttpStatusCode.Conflict;
        factory.Reports.Report = factory.Reports.Report with { Revision = 3, Status = "resolved", Tags = ["fixed"], Resolution = "Résolution concurrente." };
        using var response = await browser.PostAsync(Save, AdminFactory.Form(SaveForm(token)));
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("La version actuelle a été rechargée", WebUtility.HtmlDecode(html));
        Assert.Equal("3", AdminFactory.Input(html, "ExpectedRevision"));
        Assert.Equal("fixed", AdminFactory.Input(html, "Input.Tags"));
        Assert.Contains("Résolution concurrente.", WebUtility.HtmlDecode(html));
        AdminFactory.NoSecrets(html);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpstreamFailuresShowSafeRecoveryGuidance(bool invalidJson)
    {
        await using var factory = EnabledFactory();
        if (invalidJson) factory.Reports.InvalidJson = true;
        else factory.Reports.ReadError = HttpStatusCode.ServiceUnavailable;
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync("/BugReports");
        Assert.Contains("role=\"alert\"", html);
        Assert.Contains("Réessayer", WebUtility.HtmlDecode(html));
        AdminFactory.NoSecrets(html);
        using var screenshot = await browser.GetAsync(Screenshot);
        Assert.Equal(HttpStatusCode.BadGateway, screenshot.StatusCode);
        AdminFactory.NoSecrets(await screenshot.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("http://reports.example")]
    [InlineData("https://secret@reports.example")]
    [InlineData("https://reports.example?key=secret")]
    public void UnsafeOutboundDestinationsFailStartup(string baseUrl)
    {
        using var factory = EnabledFactory();
        factory.Settings["ReportsApi:BaseUrl"] = baseUrl;
        Assert.Throws<OptionsValidationException>(() => factory.Browser());
    }

    [Fact]
    public void RelativeCertificateAuthorityPathFailsStartup()
    {
        using var factory = EnabledFactory();
        factory.Settings["ReportsApi:CertificateAuthorityFile"] = "relative/ca.pem";
        Assert.Throws<OptionsValidationException>(() => factory.Browser());
    }

    private static Dictionary<string, string> SaveForm(string token) => new()
    {
        ["__RequestVerificationToken"] = token, ["ExpectedRevision"] = "1", ["Input.Status"] = "in_progress",
        ["Input.Tags"] = "urgent", ["Input.Resolution"] = ""
    };
}
