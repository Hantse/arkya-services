using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;

namespace Arkya.Admin.Tests;

public sealed class CharacterDetailEndpointTests
{
    private static string Path => $"/Character/{AccountsStub.CharacterId:D}";

    [Fact]
    public async Task DetailsRequireAnAuthenticatedOperator()
    {
        await using var factory = new AccountsFactory();
        using var browser = factory.Browser();
        Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync(Path)).StatusCode);
        Assert.Empty(factory.Upstream.Calls);
    }

    [Theory]
    [InlineData("/Character")]
    [InlineData("/Character/00000000-0000-0000-0000-000000000000")]
    [InlineData("/Character/not-a-guid")]
    public async Task MissingOrInvalidCharacterIdsDoNotReachTheService(string path)
    {
        await using var factory = new AccountsFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync(path)).StatusCode);
        Assert.Empty(factory.Upstream.Calls);
    }

    [Fact]
    public async Task DisabledDetailIsNotFoundWithoutCallingCharacters()
    {
        await using var factory = new AccountsFactory();
        factory.Settings["CharactersAdminApi:Enabled"] = "false";
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync(Path)).StatusCode);
        Assert.Empty(factory.Upstream.Calls);
    }

    [Fact]
    public async Task DetailOpensOverviewByDefaultAndKeepsItsRealmWhenSwitchingSections()
    {
        await using var factory = new AccountsFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        using var response = await browser.GetAsync(Path + "?realmId=beta");
        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.CacheControl?.NoStore);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("data-character-section=\"overview\"", html);
        foreach (var section in new[] { "overview", "stats", "quests", "bags", "bank", "equipment", "appearance" })
        {
            var links = Regex.Matches(WebUtility.HtmlDecode(html), "href=\"([^\"]+)\"")
                .Cast<Match>().Select(match => new Uri(new Uri("https://localhost"), match.Groups[1].Value));
            Assert.Contains(links, link => link.AbsolutePath == Path &&
                QueryHelpers.ParseQuery(link.Query)["section"].ToString() == section &&
                QueryHelpers.ParseQuery(link.Query)["realmId"].ToString() == "beta");
        }
        NoSecrets(html);
    }

    [Fact]
    public async Task AbsentOrWrongRealmCharacterIsNotFound()
    {
        await using var factory = new AccountsFactory();
        factory.Upstream.ReadError = HttpStatusCode.NotFound;
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        using var response = await browser.GetAsync(Path + "?realmId=lerya");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("lerya", QueryHelpers.ParseQuery(Assert.Single(factory.Upstream.Calls).Uri.Query)["realmId"].ToString());
        NoSecrets(await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("section=unknown")]
    [InlineData("realmId=invalid%2Frealm")]
    [InlineData("questPage=0")]
    [InlineData("questPage=invalid")]
    [InlineData("questPage=100001")]
    [InlineData("questPageSize=0")]
    [InlineData("questPageSize=101")]
    public async Task InvalidDetailFiltersDoNotReachTheService(string query)
    {
        await using var factory = new AccountsFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.GetAsync(Path + "?" + query)).StatusCode);
        Assert.Empty(factory.Upstream.Calls);
    }

    [Theory]
    [InlineData("overview", "Character overview")]
    [InlineData("stats", "Statistics and progression")]
    [InlineData("quests", "Quest history")]
    [InlineData("bags", "Bags")]
    [InlineData("bank", "Bank")]
    [InlineData("equipment", "Equipment")]
    [InlineData("appearance", "Appearance")]
    public async Task EachSectionLoadsTheReadOnlyCharacterDossier(string section, string heading)
    {
        await using var factory = new AccountsFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync(Path + "?section=" + section);
        Assert.Contains($"data-character-section=\"{section}\"", html);
        Assert.Contains(heading, html);
        Assert.Contains("&lt;script&gt;character&lt;/script&gt;", html);
        Assert.DoesNotContain("<script>character", html);
        Assert.Equal(HttpMethod.Get, Assert.Single(factory.Upstream.Calls).Method);
        NoSecrets(html);
    }

    [Fact]
    public async Task QuestHistoryShowsActiveAndCompletedRecordsAndKeepsItsScopeWhenPaging()
    {
        await using var factory = new AccountsFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync(Path + "?section=quests&realmId=beta&questPage=2&questPageSize=10");
        Assert.Contains("active-quest", html);
        Assert.Contains("completed-quest", html);
        Assert.Contains("In progress", html);
        Assert.Contains("Completed", html);
        var query = QueryHelpers.ParseQuery(Assert.Single(factory.Upstream.Calls).Uri.Query);
        Assert.Equal("2", query["questPage"].ToString());
        Assert.Equal("10", query["questPageSize"].ToString());
        foreach (var (label, number) in new[] { ("Previous", 1), ("Next", 3) })
        {
            var parameters = QueryHelpers.ParseQuery(new Uri(new Uri("https://localhost"), Href(html, label)).Query);
            Assert.Equal("quests", parameters["section"].ToString());
            Assert.Equal("beta", parameters["realmId"].ToString());
            Assert.Equal(number.ToString(), parameters["questPage"].ToString());
            Assert.Equal("10", parameters["questPageSize"].ToString());
        }
        NoSecrets(html);
    }

    [Fact]
    public async Task QuestIdentifiersAreEncoded()
    {
        await using var factory = new AccountsFactory();
        var detail = AccountsStub.SavedCharacter();
        factory.Upstream.CharacterDetail = detail with
        {
            Quests = detail.Quests with { Items = [detail.Quests.Items[0] with { QuestId = "<script>quest</script>" }] }
        };
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync(Path + "?section=quests");
        Assert.Contains("&lt;script&gt;quest&lt;/script&gt;", html);
        Assert.DoesNotContain("<script>quest", html);
    }

    [Theory]
    [InlineData("bags")]
    [InlineData("bank")]
    [InlineData("equipment")]
    public async Task UnsavedStorageIsExplicitlyUnavailableInsteadOfBeingPresentedAsEmpty(string section)
    {
        await using var factory = new AccountsFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = WebUtility.HtmlDecode(await browser.GetStringAsync(Path + "?section=" + section));
        Assert.Contains("not yet persisted", html);
        Assert.DoesNotContain("0 item", html);
        Assert.DoesNotContain("No items", html);
        NoSecrets(html);
    }

    [Theory]
    [InlineData("not_saved")]
    [InlineData("unsupported")]
    [InlineData("invalid")]
    public async Task MissingOrUnreadableSnapshotDoesNotInventZeroStatistics(string status)
    {
        await using var factory = new AccountsFactory();
        factory.Upstream.CharacterDetail = AccountsStub.SavedCharacter() with
        {
            Gameplay = new(status, null, null, [], null, null)
        };
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync(Path + "?section=stats");
        Assert.Contains("data-character-section=\"stats\"", html);
        Assert.DoesNotContain("0 / 0", html);
        Assert.DoesNotContain("<dd>0</dd>", html);
        Assert.DoesNotContain("role=\"alert\"", html);
        NoSecrets(html);
    }

    [Fact]
    public async Task SavedZeroHealthRemainsVisibleAsARealStatistic()
    {
        await using var factory = new AccountsFactory();
        var detail = AccountsStub.SavedCharacter();
        factory.Upstream.CharacterDetail = detail with
        {
            Gameplay = detail.Gameplay with { Vitals = detail.Gameplay.Vitals! with { Health = 0 } }
        };
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync(Path + "?section=stats");
        Assert.Contains("Health", html);
        Assert.Matches(@"0\s*/\s*100", html);
        Assert.Contains("Mage", html);
        Assert.DoesNotContain("role=\"alert\"", html);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DependencyOrMalformedResponseShowsASafeRecoverableError(bool malformed)
    {
        await using var factory = new AccountsFactory();
        if (malformed) factory.Upstream.CharacterDetailJson = "{}";
        else factory.Upstream.ReadError = HttpStatusCode.ServiceUnavailable;
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync(Path + "?section=stats");
        Assert.Contains("role=\"alert\"", html);
        Assert.Contains("Try again", html);
        Assert.DoesNotContain("data-character-section=\"stats\"", html);
        NoSecrets(html);
    }

    [Fact]
    public async Task UnexpectedRealmResponseIsNotRendered()
    {
        await using var factory = new AccountsFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync(Path + "?realmId=lerya");
        Assert.Contains("role=\"alert\"", html);
        Assert.DoesNotContain("&lt;script&gt;character&lt;/script&gt;", html);
        Assert.DoesNotContain("data-character-section=", html);
        NoSecrets(html);
    }

    private static string Href(string html, string label)
    {
        var anchor = Assert.Single(Regex.Matches(WebUtility.HtmlDecode(html), @"<a\b[^>]*>.*?</a>", RegexOptions.Singleline)
            .Cast<Match>().Select(match => match.Value), value => Regex.IsMatch(value, @">\s*" + Regex.Escape(label) + @"\s*</a>"));
        return Regex.Match(anchor, "href=\"([^\"]*)\"").Groups[1].Value;
    }

    private static void NoSecrets(string html)
    {
        AdminFactory.NoSecrets(html);
        Assert.DoesNotContain(AccountsStub.Key, html);
        Assert.DoesNotContain("private upstream details", html);
    }
}
