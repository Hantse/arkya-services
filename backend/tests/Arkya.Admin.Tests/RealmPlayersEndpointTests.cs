using System.Net;
using System.Text.RegularExpressions;
using Arkya.Admin.Accounts;
using Arkya.Admin.Realms;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Arkya.Admin.Tests;

public sealed class RealmPlayersEndpointTests
{
    private const string Path = "/Realms/terres-aube";
    private const string Central = "https://admin.arkya.online";
    private const string Dedicated = "https://terres-aube.admin.arkya.online";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DirectoryScopesSearchPresenceAndPaginationToItsRealm(bool onlineOnly)
    {
        await using var factory = new RealmPlayersFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync(Path + "?section=players&search=%20Ali%20&pageNumber=2&pageSize=25&onlineOnly=" + onlineOnly);
        var call = Assert.Single(factory.Upstream.Calls);
        var query = QueryHelpers.ParseQuery(call.Uri.Query);
        Assert.Equal("/admin/characters", call.Uri.AbsolutePath);
        Assert.Equal("terres-aube", query["realmId"].ToString());
        Assert.Equal("Ali", query["search"].ToString());
        Assert.Equal("2", query["page"].ToString());
        Assert.Equal("25", query["pageSize"].ToString());
        Assert.Equal(onlineOnly.ToString().ToLowerInvariant(), query["onlineOnly"].ToString());
        Assert.Empty(query["accountId"].ToString());
        Assert.Equal("/admin/realms/terres-aube", Assert.Single(factory.Realms.Calls).Path);
        Assert.Contains("data-realm-section=\"players\"", html);
        Assert.Contains("80 character(s)", html);
        Assert.Contains("&lt;script&gt;character&lt;/script&gt;", html);
        Assert.DoesNotContain("<script>character", html);
        Assert.Contains($"href=\"/Character/{AccountsStub.CharacterId:D}?realmId=terres-aube\"", html);
        foreach (var (label, page) in new[] { ("Refresh", 2), ("Previous", 1), ("Next", 3) })
        {
            var href = Href(html, label);
            var parameters = QueryHelpers.ParseQuery(new Uri(new Uri("https://localhost"), href).Query);
            Assert.Equal("players", parameters["section"].ToString());
            Assert.Equal("Ali", parameters["search"].ToString());
            Assert.Equal(onlineOnly, parameters.TryGetValue("onlineOnly", out var presence) && bool.TryParse(presence.ToString(), out var active) && active);
            Assert.Equal(page.ToString(), parameters["pageNumber"].ToString());
            Assert.True(!parameters.TryGetValue("pageSize", out var size) || size.ToString() == "25");
        }
        NoSecrets(html);
    }

    [Theory]
    [InlineData("overview")]
    [InlineData("settings")]
    [InlineData("maintenance")]
    [InlineData("game")]
    [InlineData("history")]
    [InlineData("unknown")]
    public async Task OtherSectionsDoNotLoadTheCharacterDirectory(string section)
    {
        await using var factory = new RealmPlayersFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync(Path + "?section=" + section);
        Assert.Empty(factory.Upstream.Calls);
        Assert.DoesNotContain("<script>character", html);
        if (section == "game")
        {
            var query = QueryHelpers.ParseQuery(new Uri(new Uri("https://localhost"), Href(html, "this realm's online characters")).Query);
            Assert.Equal("players", query["section"].ToString());
            Assert.Equal("true", query["onlineOnly"].ToString());
        }
    }

    [Theory]
    [InlineData("pageNumber=-1")]
    [InlineData("pageNumber=invalid")]
    [InlineData("pageSize=101")]
    [InlineData("onlineOnly=invalid")]
    [InlineData("search=first%0Asecond")]
    public async Task InvalidFiltersDoNotReachCharacters(string filter)
    {
        await using var factory = new RealmPlayersFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync(Path + "?section=players&" + filter);
        Assert.Contains("validation-summary-errors", html);
        Assert.Empty(factory.Upstream.Calls);
    }

    [Fact]
    public async Task DisabledDirectoryHasAnExplicitStateWithoutCallingCharacters()
    {
        await using var factory = new RealmPlayersFactory();
        factory.Settings["CharactersAdminApi:Enabled"] = "false";
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = WebUtility.HtmlDecode(await browser.GetStringAsync(Path + "?section=players"));
        Assert.Contains("Player viewing is disabled", html);
        Assert.DoesNotContain("None personnage", html);
        Assert.Empty(factory.Upstream.Calls);
        NoSecrets(html);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task CharacterDependencyFailureLeavesTheRealmWorkspaceAvailable(HttpStatusCode status)
    {
        await using var factory = new RealmPlayersFactory();
        factory.Upstream.ReadError = status;
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = WebUtility.HtmlDecode(await browser.GetStringAsync(Path + "?section=players"));
        Assert.Contains("Les Terres d'Aube", html);
        Assert.Contains("Realm players are unavailable", html);
        Assert.Contains("Try again", html);
        Assert.DoesNotContain("None personnage", html);
        NoSecrets(html);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MismatchedRealmOrPresenceResponseIsRejectedWithoutLeakingRowsOrTotals(bool wrongPresence)
    {
        await using var factory = new RealmPlayersFactory();
        if (wrongPresence) factory.Upstream.CharacterOnline = false;
        else factory.Upstream.CharactersRealmId = "another-realm";
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = WebUtility.HtmlDecode(await browser.GetStringAsync(Path + "?section=players&onlineOnly=true"));
        Assert.Contains("Realm players are unavailable", html);
        Assert.DoesNotContain("<script>character", html);
        Assert.DoesNotContain("80 character(s)", html);
        Assert.DoesNotContain("None personnage", html);
        NoSecrets(html);
    }

    [Fact]
    public async Task EmptyDirectoryKeepsItsFiltersAndPagination()
    {
        await using var factory = new RealmPlayersFactory();
        factory.Upstream.Empty = true;
        factory.Upstream.Total = 0;
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = WebUtility.HtmlDecode(await browser.GetStringAsync(Path + "?section=players&search=Absent&onlineOnly=true"));
        Assert.Contains("No characters on this page", html);
        Assert.Contains("0 character(s)", html);
        var query = QueryHelpers.ParseQuery(new Uri(new Uri("https://localhost"), Href(html, "First page")).Query);
        Assert.Equal("players", query["section"].ToString());
        Assert.Equal("Absent", query["search"].ToString());
        Assert.Equal("True", query["onlineOnly"].ToString(), ignoreCase: true);
    }

    [Fact]
    public async Task DedicatedWorkspaceKeepsAccountManagementCentralAndRejectsRealmOverrides()
    {
        await using var factory = new RealmPlayersFactory();
        factory.Settings["RealmSites:Enabled"] = "true";
        using var browser = factory.Browser();
        browser.BaseAddress = new Uri(Central);
        await AdminFactory.Login(browser);
        var html = WebUtility.HtmlDecode(await browser.GetStringAsync(Dedicated + "/?section=players"));
        Assert.Contains("href=\"" + Central + "/Account/" + AccountsStub.Id.ToString("D") + "\"", html);
        Assert.Contains("Manage account in general administration", html);
        Assert.Contains($"href=\"{Central}/Character/{AccountsStub.CharacterId:D}?realmId=terres-aube\"", html);
        Assert.Equal("terres-aube", QueryHelpers.ParseQuery(Assert.Single(factory.Upstream.Calls).Uri.Query)["realmId"].ToString());
        factory.Realms.Calls.Clear();
        factory.Upstream.Calls.Clear();
        foreach (var path in new[] { "/?section=players&realmId=another-realm", "/Realms/another-realm?section=players" })
            Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync(Dedicated + path)).StatusCode);
        Assert.Empty(factory.Realms.Calls);
        Assert.Empty(factory.Upstream.Calls);
    }

    [Fact]
    public async Task DirectoryRequiresAnAuthenticatedOperatorAndEnabledRealms()
    {
        await using var factory = new RealmPlayersFactory();
        using var anonymous = factory.Browser();
        Assert.Equal(HttpStatusCode.Redirect, (await anonymous.GetAsync(Path + "?section=players")).StatusCode);
        Assert.Empty(factory.Realms.Calls);
        Assert.Empty(factory.Upstream.Calls);
        await using var disabled = new RealmPlayersFactory();
        disabled.Settings["RealmsApi:Enabled"] = "false";
        using var browser = disabled.Browser();
        await AdminFactory.Login(browser);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync(Path + "?section=players")).StatusCode);
        Assert.Empty(disabled.Realms.Calls);
        Assert.Empty(disabled.Upstream.Calls);
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
    }
}

internal sealed class RealmPlayersFactory : WebApplicationFactory<Arkya.Admin.Program>
{
    public RealmsStub Realms { get; } = new();
    public AccountsStub Upstream { get; } = new() { CharactersRealmId = "terres-aube" };
    public Dictionary<string, string?> Settings { get; } = [];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Admin:Password"] = AdminFactory.Password, ["Admin:DiscordEnabled"] = "false",
                ["ContentApi:Enabled"] = "false", ["ReportsApi:Enabled"] = "false", ["InferenceApi:Enabled"] = "false",
                ["IdentityAdminApi:Enabled"] = "false", ["AllowedHosts"] = "admin.arkya.online;*.admin.arkya.online;localhost",
                ["RealmsApi:Enabled"] = "true", ["RealmsApi:BaseUrl"] = "https://realms.example/", ["RealmsApi:AdminKey"] = AdminFactory.RealmsAdminKey,
                ["CharactersAdminApi:Enabled"] = "true", ["CharactersAdminApi:BaseUrl"] = "https://characters.example/", ["CharactersAdminApi:AdminKey"] = AccountsStub.Key
            });
            configuration.AddInMemoryCollection(Settings);
        });
        builder.ConfigureServices(services =>
        {
            services.AddTestSessions();
            services.AddHttpClient<RealmsAdminClient>().ConfigurePrimaryHttpMessageHandler(() => Realms);
            services.AddHttpClient<CharactersAdminClient>().ConfigurePrimaryHttpMessageHandler(() => Upstream);
        });
    }

    public HttpClient Browser() => CreateClient(new WebApplicationFactoryClientOptions
    { BaseAddress = new("https://localhost"), AllowAutoRedirect = false, HandleCookies = true });
}
