using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;

namespace Arkya.Admin.Tests;

public sealed class PublicationListFilterTests
{
    private const string Filters = "kind=release-note&language=en&q=b%C3%AAta%20%2B%20%5Bx%5D%3F%20%26%20kind%3Dnews&gameVersion=0.4-beta%2B1&from=2026-10-01&to=2026-10-24&sort=oldest&offset=48&limit=24";

    [Fact]
    public async Task DefaultListSelectsNewsAndOffersTwoDistinctTabs()
    {
        await using var factory = new AdminFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync("/Publications");
        var query = QueryHelpers.ParseQuery(Assert.Single(factory.Content.PostQueries));
        Assert.Equal("news", query["kind"].ToString());
        Assert.Equal("12", query["limit"].ToString());
        var tabs = Regex.Match(html, "<nav[^>]*aria-label=\"Publication type\"[^>]*>(.*?)</nav>", RegexOptions.Singleline).Groups[1].Value;
        Assert.Equal(2, Links(tabs).Count());
        Assert.Contains(Links(tabs), link => link.Text == "News" && link.Query["kind"] == "news");
        Assert.Contains(Links(tabs), link => link.Text == "Release notes" && link.Query["kind"] == "release-note");
        Assert.Matches("<a(?=[^>]*aria-current=\"page\")[^>]*>\\s*News\\s*</a>", tabs);
        Assert.DoesNotContain("All types", html);
    }

    [Fact]
    public async Task CombinedFiltersAreLiteralAndSurviveNavigation()
    {
        await using var factory = new AdminFactory();
        factory.Content.PostsHaveMore = true;
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync("/Publications?" + Filters);
        var query = QueryHelpers.ParseQuery(Assert.Single(factory.Content.PostQueries));
        AssertFilters(query);
        Assert.Equal("48", query["offset"].ToString());
        Assert.Equal("bêta + [x]? & kind=news", AdminFactory.Input(html, "q"));
        var links = Links(html).ToArray();
        foreach (var (text, offset) in new[] { ("Previous", "24"), ("Next", "72") })
        {
            var link = Assert.Single(links, item => item.Text.Contains(text, StringComparison.Ordinal));
            AssertFilters(link.Query);
            Assert.Equal(offset, link.Query["offset"].ToString());
        }
        var news = Assert.Single(links, item => item.Text == "News" && item.Path == "/Publications");
        AssertFilters(news.Query, news: true);
        Assert.True(!news.Query.ContainsKey("offset") || news.Query["offset"] == "0");
        Assert.False(news.Query.ContainsKey("gameVersion"));
        var clear = Assert.Single(links, item => item.Text == "Clear filters");
        Assert.Equal("release-note", clear.Query["kind"].ToString());
        foreach (var key in new[] { "q", "gameVersion", "from", "to", "language" }) Assert.False(clear.Query.ContainsKey(key));
        var create = Assert.Single(links, item => item.Text == "Create a draft");
        Assert.Equal("release-note", create.Query["kind"].ToString());
        Assert.Equal("en", create.Query["language"].ToString());
        AdminFactory.NoSecrets(html);
    }

    [Fact]
    public async Task RetryRetainsTheCompleteSelectionWithoutShowingPrivateDiagnostics()
    {
        await using var factory = new AdminFactory();
        factory.Content.PostReadError = HttpStatusCode.ServiceUnavailable;
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync("/Publications?" + Filters);
        var retry = Assert.Single(Links(html), item => item.Text == "Try again");
        AssertFilters(retry.Query);
        Assert.Equal("48", retry.Query["offset"].ToString());
        Assert.Contains("unavailable", html);
        AdminFactory.NoSecrets(html);
    }

    [Theory]
    [InlineData("q=first%0Asecond")]
    [InlineData("q=%7F")]
    [InlineData("kind=release-note&gameVersion=../unsafe")]
    [InlineData("from=2026-02-29")]
    [InlineData("from=2026-1-01")]
    [InlineData("from=0000-01-01")]
    [InlineData("to=2026-10-24T00%3A00%3A00Z")]
    [InlineData("from=2026-10-25&to=2026-10-24")]
    [InlineData("sort=updated")]
    [InlineData("limit=0")]
    [InlineData("limit=invalid")]
    [InlineData("offset=invalid")]
    public async Task InvalidFiltersNeverReachThePrivateApi(string query)
    {
        await using var factory = new AdminFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync("/Publications?" + query);
        Assert.Contains("validation-summary-errors", html);
        Assert.Empty(factory.Content.Calls);
        AdminFactory.NoSecrets(html);
    }

    [Theory]
    [InlineData("q", 100)]
    [InlineData("gameVersion", 64)]
    public async Task OversizedFiltersAreRejectedBeforeReadingContent(string field, int maximum)
    {
        await using var factory = new AdminFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync($"/Publications?kind=release-note&{field}={new string('x', maximum + 1)}");
        Assert.Contains("validation-summary-errors", html);
        Assert.Empty(factory.Content.Calls);
    }

    [Theory]
    [InlineData(12)]
    [InlineData(24)]
    [InlineData(48)]
    [InlineData(10)]
    public async Task PageSizesAreForwardedAndRemainSelected(int limit)
    {
        await using var factory = new AdminFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync($"/Publications?limit={limit}");
        Assert.Equal(limit.ToString(), QueryHelpers.ParseQuery(Assert.Single(factory.Content.PostQueries))["limit"].ToString());
        Assert.Matches($"<option(?=[^>]*value=\"{limit}\")(?=[^>]*selected)[^>]*>", html);
        foreach (var size in new[] { 12, 24, 48 }) Assert.Contains($"value=\"{size}\"", html);
    }

    [Fact]
    public async Task MaximumOffsetCannotGenerateAnInvalidNextPage()
    {
        await using var factory = new AdminFactory();
        factory.Content.PostsHaveMore = true;
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync("/Publications?offset=9996&limit=12");
        Assert.DoesNotContain(Links(html), link => link.Text.Contains("Next", StringComparison.Ordinal));
        Assert.Contains(Links(html), link => link.Text.Contains("Previous", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SearchTextIsEncodedInInputsAndLinks()
    {
        const string search = "\"><script>publication-search</script>";
        await using var factory = new AdminFactory();
        factory.Content.PostsHaveMore = true;
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        using var response = await browser.GetAsync("/Publications?q=" + Uri.EscapeDataString(search));
        var html = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("<script>publication-search", html);
        Assert.Equal(search, AdminFactory.Input(html, "q"));
        Assert.Equal(search, Assert.Single(Links(html), item => item.Text.Contains("Next", StringComparison.Ordinal)).Query["q"].ToString());
        Assert.Contains("script-src 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Theory]
    [InlineData("news", "fr")]
    [InlineData("release-note", "en")]
    public async Task CreatingFromATabPrefillsTypeAndLanguageAndReturnsToThatTab(string kind, string language)
    {
        await using var factory = new AdminFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync($"/Publication?kind={kind}&language={language}");
        Assert.Matches($"<option(?=[^>]*value=\"{kind}\")(?=[^>]*selected)[^>]*>", html);
        Assert.Matches($"<option(?=[^>]*value=\"{language}\")(?=[^>]*selected)[^>]*>", html);
        Assert.Equal(kind, Assert.Single(Links(html), link => link.Path == "/Publications" && link.Query.ContainsKey("kind")).Query["kind"].ToString());
        Assert.Empty(factory.Content.Calls);
    }

    private static void AssertFilters(Dictionary<string, StringValues> query, bool news = false)
    {
        Assert.Equal(news ? "news" : "release-note", query["kind"].ToString());
        Assert.Equal("en", query["language"].ToString());
        Assert.Equal("bêta + [x]? & kind=news", query["q"].ToString());
        if (!news) Assert.Equal("0.4-beta+1", query["gameVersion"].ToString());
        Assert.Equal("2026-10-01", query["from"].ToString());
        Assert.Equal("2026-10-24", query["to"].ToString());
        Assert.Equal("oldest", query["sort"].ToString());
        Assert.Equal("24", query["limit"].ToString());
    }

    private static IEnumerable<(string Text, string Path, Dictionary<string, StringValues> Query)> Links(string html)
    {
        foreach (Match match in Regex.Matches(html, "<a\\b[^>]*href=\"([^\"]*)\"[^>]*>(.*?)</a>", RegexOptions.Singleline))
        {
            var uri = new Uri(new Uri("https://localhost"), WebUtility.HtmlDecode(match.Groups[1].Value));
            var text = WebUtility.HtmlDecode(Regex.Replace(match.Groups[2].Value, "<[^>]*>", "")).Trim().Trim('←', '→').Trim();
            yield return (text, uri.AbsolutePath, QueryHelpers.ParseQuery(uri.Query));
        }
    }
}
