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
        Assert.Contains("Report management is disabled", WebUtility.HtmlDecode(html));
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
        var filters = new Dictionary<string, string?>
        {
            ["tag"] = " URGENT ", ["status"] = "triaged", ["offset"] = "20", ["limit"] = "20", ["kind"] = kind,
            ["search"] = "souche & rivage", ["scene"] = "StarterIsland", ["clientVersion"] = "0.1.0",
            ["platform"] = "WebGL", ["createdFrom"] = "2026-10-01", ["createdTo"] = "2026-10-24", ["sort"] = "updated"
        };
        var html = await browser.GetStringAsync(QueryHelpers.AddQueryString("/BugReports", filters));
        var call = Assert.Single(factory.Reports.Calls);
        var query = QueryHelpers.ParseQuery(call.Uri.Query);
        Assert.Equal("urgent", query["tag"].ToString());
        Assert.Equal("triaged", query["status"].ToString());
        Assert.Equal("20", query["offset"].ToString());
        Assert.Equal("20", query["limit"].ToString());
        Assert.Equal(kind, query["kind"].ToString());
        foreach (var key in new[] { "search", "scene", "clientVersion", "platform", "createdFrom", "createdTo", "sort" })
            Assert.Equal(filters[key], query[key].ToString());
        Assert.Contains("&lt;script&gt;player-input&lt;/script&gt;", html);
        Assert.DoesNotContain("<script>player-input", html);
        Assert.Contains("offset=40", html);
        Assert.Contains("Previous", WebUtility.HtmlDecode(html));
        var nextLink = Regex.Matches(html, "href=\"([^\"]+)\"").Select(match => WebUtility.HtmlDecode(match.Groups[1].Value))
            .Select(href => QueryHelpers.ParseQuery(new Uri(new Uri("https://localhost"), href).Query))
            .First(parameters => parameters.TryGetValue("offset", out var offset) && offset == "40");
        Assert.Equal(kind, nextLink["kind"].ToString());
        Assert.Equal("urgent", nextLink["tag"].ToString());
        Assert.Equal("triaged", nextLink["status"].ToString());
        Assert.Equal("20", nextLink["limit"].ToString());
        foreach (var key in new[] { "search", "scene", "clientVersion", "platform", "createdFrom", "createdTo", "sort" })
            Assert.Equal(filters[key], nextLink[key].ToString());
        AdminFactory.NoSecrets(html);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?kind=")]
    public async Task DefaultListShowsTheBugsTab(string query)
    {
        await using var factory = EnabledFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync("/BugReports" + query);
        var call = Assert.Single(factory.Reports.Calls);
        Assert.Equal("bug", QueryHelpers.ParseQuery(call.Uri.Query)["kind"].ToString());
        Assert.Equal("newest", QueryHelpers.ParseQuery(call.Uri.Query)["sort"].ToString());
        Assert.DoesNotContain("All types", html);
        var tabs = ReportLinks(html).Where(link => link.Text is "Bugs" or "Suggestions").ToArray();
        Assert.Equal(2, tabs.Length);
        Assert.Equal("bug", Assert.Single(tabs, link => link.Text == "Bugs").Query["kind"].ToString());
        Assert.Equal("suggestion", Assert.Single(tabs, link => link.Text == "Suggestions").Query["kind"].ToString());
        Assert.Matches("<a[^>]*aria-current=\"page\"[^>]*>\\s*Bugs\\s*</a>", html);
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
    [InlineData("?sort=unknown")]
    [InlineData("?sort=Newest")]
    [InlineData("?createdFrom=not-a-date")]
    [InlineData("?createdTo=2026-02-30")]
    [InlineData("?createdFrom=2026-10-25&createdTo=2026-10-24")]
    [InlineData("?search=foo%0Abar")]
    [InlineData("?scene=Starter%0DIsland")]
    [InlineData("?clientVersion=0.1%00beta")]
    [InlineData("?platform=Web%09GL")]
    public async Task InvalidListParametersDoNotReachTheApi(string query)
    {
        await using var factory = EnabledFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        Assert.Contains("validation-summary-errors", await browser.GetStringAsync("/BugReports" + query));
        Assert.Empty(factory.Reports.Calls);
    }

    [Theory]
    [InlineData("bug", "", "No bugs yet")]
    [InlineData("suggestion", "", "No suggestions yet")]
    [InlineData("bug", "&search=collision", "No bugs match these filters")]
    [InlineData("suggestion", "&status=resolved", "No suggestions match these filters")]
    public async Task EmptyListHasAKindSpecificState(string kind, string filters, string expected)
    {
        await using var factory = EnabledFactory();
        factory.Reports.Empty = true;
        factory.Reports.Total = 0;
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        Assert.Contains(expected, WebUtility.HtmlDecode(await browser.GetStringAsync("/BugReports?kind=" + kind + filters)));
    }

    [Fact]
    public async Task TabsKeepFiltersAndResetPaginationWhileRefreshKeepsTheCurrentPage()
    {
        await using var factory = EnabledFactory();
        factory.Reports.Total = 250;
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var filters = new Dictionary<string, string?>
        {
            ["kind"] = "bug", ["tag"] = "urgent", ["status"] = "triaged", ["search"] = "souche & rivage",
            ["scene"] = "StarterIsland", ["clientVersion"] = "0.1.0", ["platform"] = "WebGL",
            ["createdFrom"] = "2026-10-01", ["createdTo"] = "2026-10-24", ["sort"] = "oldest",
            ["offset"] = "40", ["limit"] = "10"
        };
        var html = await browser.GetStringAsync(QueryHelpers.AddQueryString("/BugReports", filters));
        var links = ReportLinks(html).ToArray();
        var suggestions = Assert.Single(links, link => link.Text == "Suggestions").Query;
        Assert.Equal("suggestion", suggestions["kind"].ToString());
        Assert.True(!suggestions.TryGetValue("offset", out var offset) || offset == "0");
        var refresh = Assert.Single(links, link => link.Text == "Refresh").Query;
        Assert.Equal("40", refresh["offset"].ToString());
        foreach (var key in filters.Keys.Where(key => key is not "kind" and not "offset"))
        {
            Assert.Equal(filters[key], suggestions[key].ToString());
            Assert.Equal(filters[key], refresh[key].ToString());
        }
        var tag = Assert.Single(links, link => link.Text == "urgent").Query;
        Assert.Equal("bug", tag["kind"].ToString());
        Assert.True(!tag.TryGetValue("offset", out offset) || offset == "0");
        foreach (var key in filters.Keys.Where(key => key != "offset")) Assert.Equal(filters[key], tag[key].ToString());
        var form = Regex.Match(html, "<form[^>]*method=\"get\"[^>]*>(.*?)</form>", RegexOptions.Singleline | RegexOptions.IgnoreCase).Groups[1].Value;
        Assert.NotEmpty(form);
        Assert.DoesNotContain("name=\"Offset\"", form, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("bug", AdminFactory.Input(form, "Kind"));
    }

    [Fact]
    public async Task RetryKeepsTheFailedPageAndItsFilters()
    {
        await using var factory = EnabledFactory();
        factory.Reports.ReadError = HttpStatusCode.ServiceUnavailable;
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var filters = new Dictionary<string, string?>
        {
            ["kind"] = "suggestion", ["tag"] = "urgent", ["status"] = "triaged", ["search"] = "bridge",
            ["scene"] = "StarterIsland", ["clientVersion"] = "0.1.0", ["platform"] = "WebGL",
            ["createdFrom"] = "2026-10-01", ["createdTo"] = "2026-10-24", ["sort"] = "updated",
            ["offset"] = "40", ["limit"] = "10"
        };
        var html = await browser.GetStringAsync(QueryHelpers.AddQueryString("/BugReports", filters));
        var retry = Assert.Single(ReportLinks(html), link => link.Text == "Try again").Query;
        foreach (var (key, value) in filters) Assert.Equal(value, retry[key].ToString());
    }

    [Fact]
    public async Task PaginationShowsResultRangeNumberedPagesAndDistantEndpoints()
    {
        await using var factory = EnabledFactory();
        factory.Reports.Total = 250;
        factory.Reports.ListItems = Enumerable.Repeat(factory.Reports.Report, 10).ToArray();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync("/BugReports?kind=suggestion&offset=40&limit=10&sort=updated");
        Assert.Matches("41\\s*[–-]\\s*50\\s+of\\s+250", WebUtility.HtmlDecode(html));
        var navigation = Regex.Match(html, "<nav[^>]*aria-label=\"Report pages\"[^>]*>(.*?)</nav>", RegexOptions.Singleline).Groups[1].Value;
        Assert.NotEmpty(navigation);
        var links = ReportLinks(navigation).ToArray();
        Assert.Equal("30", Assert.Single(links, link => link.Text == "Previous").Query["offset"].ToString());
        Assert.Equal("50", Assert.Single(links, link => link.Text == "Next").Query["offset"].ToString());
        Assert.Contains(links, link => !link.Query.TryGetValue("offset", out var offset) || offset == "0");
        Assert.Contains(links, link => link.Query.TryGetValue("offset", out var offset) && offset == "240");
        Assert.True(links.Count(link => int.TryParse(link.Text, out _)) >= 3);
        Assert.Matches("aria-current=\"page\"[^>]*>\\s*5\\s*<", navigation);
        foreach (var link in links)
        {
            Assert.Equal("suggestion", link.Query["kind"].ToString());
            Assert.Equal("10", link.Query["limit"].ToString());
            Assert.Equal("updated", link.Query["sort"].ToString());
        }
    }

    [Theory]
    [InlineData(0, 1, false, false)]
    [InlineData(20, 21, true, false)]
    [InlineData(100000, 100002, true, false)]
    public async Task PaginationRespectsTheLastResultAndMaximumOffset(int offset, long total, bool previous, bool next)
    {
        await using var factory = EnabledFactory();
        factory.Reports.Total = total;
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync($"/BugReports?offset={offset}&limit=20");
        var query = QueryHelpers.ParseQuery(Assert.Single(factory.Reports.Calls).Uri.Query);
        Assert.Equal(offset.ToString(), query["offset"].ToString());
        var links = ReportLinks(html).ToArray();
        Assert.Equal(previous, links.Any(link => link.Text == "Previous"));
        Assert.Equal(next, links.Any(link => link.Text == "Next"));
        Assert.All(links.Where(link => link.Query.ContainsKey("offset")), link =>
            Assert.InRange(int.Parse(link.Query["offset"].ToString()), 0, 100000));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(37)]
    [InlineData(100)]
    public async Task EveryValidPageSizeIsAcceptedAndPreservedInTheSelector(int limit)
    {
        await using var factory = EnabledFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync($"/BugReports?limit={limit}");
        Assert.Equal(limit.ToString(), QueryHelpers.ParseQuery(Assert.Single(factory.Reports.Calls).Uri.Query)["limit"].ToString());
        Assert.Matches($"<option(?=[^>]*value=\"{limit}\")(?=[^>]*selected)[^>]*>", html);
        foreach (var size in new[] { 10, 20, 50, 100 }) Assert.Contains($"value=\"{size}\"", html);
    }

    [Theory]
    [InlineData("search", 120)]
    [InlineData("scene", 128)]
    [InlineData("clientVersion", 64)]
    [InlineData("platform", 64)]
    public async Task OversizedTextFiltersDoNotReachTheApi(string field, int maximum)
    {
        await using var factory = EnabledFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync($"/BugReports?{field}={new string('x', maximum + 1)}");
        Assert.Contains("validation-summary-errors", html);
        Assert.Empty(factory.Reports.Calls);
    }

    [Theory]
    [InlineData("newest")]
    [InlineData("oldest")]
    [InlineData("updated")]
    public async Task SupportedSortsAreForwarded(string sort)
    {
        await using var factory = EnabledFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        await browser.GetStringAsync("/BugReports?sort=" + sort);
        Assert.Equal(sort, QueryHelpers.ParseQuery(Assert.Single(factory.Reports.Calls).Uri.Query)["sort"].ToString());
    }

    [Fact]
    public async Task SearchTextIsEncodedInTheFilterAndNavigation()
    {
        await using var factory = EnabledFactory();
        factory.Reports.Total = 80;
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        const string search = "\"><script>player-search</script>";
        var html = await browser.GetStringAsync("/BugReports?search=" + Uri.EscapeDataString(search));
        Assert.Contains("&lt;script&gt;player-search&lt;/script&gt;", html);
        Assert.DoesNotContain("<script>player-search", html);
        Assert.Equal(search, QueryHelpers.ParseQuery(Assert.Single(factory.Reports.Calls).Uri.Query)["search"].ToString());
        Assert.Equal(search, Assert.Single(ReportLinks(html), link => link.Text == "Next").Query["search"].ToString());
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
        Assert.Contains("Completed", WebUtility.HtmlDecode(list));
        var html = WebUtility.HtmlDecode(await browser.GetStringAsync(Detail));
        Assert.Contains("<dt>Type</dt><dd>Suggestion</dd>", html);
        Assert.Contains("Completed", html);
        Assert.DoesNotContain("Resolved", html);
        Assert.Contains("Response / implementation", html);
        Assert.Contains("handler=Screenshot", html);
        Assert.Contains(ReportLinks(html), link => link.Query.TryGetValue("kind", out var kind) && kind == "suggestion");
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
        Assert.Contains("Resolved", html);
        Assert.DoesNotContain("Completed", html);
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
        Assert.Contains("Report updated.", WebUtility.HtmlDecode(html));
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
        Assert.Contains("The current version has been reloaded", WebUtility.HtmlDecode(html));
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
        Assert.Contains("Try again", WebUtility.HtmlDecode(html));
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

    private static IEnumerable<(string Text, Dictionary<string, Microsoft.Extensions.Primitives.StringValues> Query)> ReportLinks(string html) =>
        Regex.Matches(html, "<a\\b[^>]*href=\"([^\"]+)\"[^>]*>(.*?)</a>", RegexOptions.Singleline)
            .Select(match => (Href: WebUtility.HtmlDecode(match.Groups[1].Value),
                Text: WebUtility.HtmlDecode(Regex.Replace(match.Groups[2].Value, "<[^>]+>", "")).Trim(' ', '\r', '\n', '←', '→')))
            .Where(link => new Uri(new Uri("https://localhost"), link.Href).AbsolutePath == "/BugReports")
            .Select(link => (link.Text, QueryHelpers.ParseQuery(new Uri(new Uri("https://localhost"), link.Href).Query)));
}
