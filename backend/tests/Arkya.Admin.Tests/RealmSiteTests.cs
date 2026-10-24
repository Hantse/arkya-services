using System.Net;
using System.Text.RegularExpressions;
using Arkya.Admin.Realms;
using Microsoft.AspNetCore.WebUtilities;

namespace Arkya.Admin.Tests;

public sealed class RealmSiteTests
{
    private const string Central = "https://admin.arkya.online";
    private const string Dedicated = "https://terres-aube.admin.arkya.online";

    private static AdminFactory Factory()
    {
        var factory = new AdminFactory { EnvironmentName = "Production" };
        factory.Settings["AllowedHosts"] = "admin.arkya.online;*.admin.arkya.online";
        factory.Settings["RealmSites:Enabled"] = "true";
        factory.Settings["RealmsApi:Enabled"] = "true";
        factory.Realms.Realm = factory.Realms.Realm with { Status = "Ready" };
        return factory;
    }

    [Fact]
    public async Task AnonymousRealmRootUsesCentralLoginAndRetainsItsSection()
    {
        await using var factory = Factory();
        using var browser = factory.Browser();
        using var response = await browser.GetAsync(Dedicated + "/?section=settings");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(Central, response.Headers.Location!.GetLeftPart(UriPartial.Authority));
        Assert.Equal("/Login", response.Headers.Location.AbsolutePath);
        Assert.Equal("/Realms/terres-aube?section=settings", QueryHelpers.ParseQuery(response.Headers.Location.Query)["ReturnUrl"].ToString());
        Assert.Empty(factory.Realms.Calls);
    }

    [Fact]
    public async Task CentralListLinksToDedicatedRealmAndLegacyLinkKeepsSection()
    {
        await using var factory = Factory();
        using var browser = factory.Browser();
        browser.BaseAddress = new Uri(Central);
        await AdminFactory.Login(browser);
        Assert.Contains("href=\"" + Dedicated + "/\"", await browser.GetStringAsync("/Realms"));
        using var response = await browser.GetAsync("/Realms/terres-aube?section=history");
        Assert.Equal(Dedicated + "/?section=history", response.Headers.Location!.AbsoluteUri);
    }

    [Fact]
    public async Task SharedAdminSessionOpensRealmWorkspaceAndRenewsAcrossHosts()
    {
        await using var factory = Factory();
        using var browser = factory.Browser();
        browser.BaseAddress = new Uri(Central);
        var token = await AdminFactory.Token(browser, "/Login");
        using var login = await browser.PostAsync("/Login", AdminFactory.Form(new()
        { ["Password"] = AdminFactory.Password, ["__RequestVerificationToken"] = token }));
        var cookies = login.Headers.GetValues("Set-Cookie").Where(cookie => cookie.StartsWith("Arkya.Admin.")).ToArray();
        Assert.Equal(2, cookies.Length);
        Assert.All(cookies, cookie =>
        {
            Assert.Contains("domain=admin.arkya.online", cookie);
            Assert.Contains("secure", cookie);
            Assert.Contains("httponly", cookie);
        });
        var html = WebUtility.HtmlDecode(await browser.GetStringAsync(Dedicated + "/"));
        Assert.Contains("Les Terres d'Aube", html);
        Assert.Contains("https://terres-aube.arkya.online/", html);
        Assert.Contains("General administration", html);
        Assert.DoesNotContain("Accounts and access", html);
        AdminFactory.NoSecrets(html);
        factory.Clock.Advance(TimeSpan.FromHours(1));
        using var renewed = await browser.GetAsync(Dedicated + "/?section=settings");
        Assert.Equal(HttpStatusCode.OK, renewed.StatusCode);
        Assert.Contains(renewed.Headers.GetValues("Set-Cookie"), cookie => cookie.StartsWith("Arkya.Admin.Refresh.RealmSites=") && cookie.Contains("domain=admin.arkya.online"));
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync(Central + "/Realms")).StatusCode);
    }

    [Theory]
    [InlineData("overview")]
    [InlineData("map")]
    [InlineData("players")]
    [InlineData("settings")]
    [InlineData("maintenance")]
    [InlineData("game")]
    [InlineData("history")]
    public async Task DedicatedRealmUsesOneSidebarForEverySectionAndKeepsCentralAndPublicLinks(string section)
    {
        await using var factory = Factory();
        using var browser = factory.Browser();
        browser.BaseAddress = new Uri(Central);
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync(Dedicated + "/?section=" + section);
        var navigation = Regex.Match(html, @"<nav\b[^>]*aria-label=""Main menu""[^>]*>.*?</nav>", RegexOptions.Singleline).Value;
        var sectionLinks = Regex.Matches(navigation, @"<a\b[^>]*href=""/\?section=[^""]*""[^>]*>.*?</a>", RegexOptions.Singleline);
        Assert.Equal(7, sectionLinks.Count);
        var active = Assert.Single(sectionLinks.Cast<Match>(), link => link.Value.Contains("aria-current=\"page\""));
        Assert.Contains("href=\"/?section=" + section + "\"", active.Value);
        Assert.Contains("data-realm-section=\"" + section + "\"", html);
        Assert.Contains("href=\"" + Central + "/Realms\"", navigation);
        Assert.Contains("href=\"" + Central + "/\"", navigation);
        Assert.Contains("href=\"https://terres-aube.arkya.online/\"", navigation);
        Assert.DoesNotContain("<nav", Regex.Match(html, @"<main\b.*?</main>", RegexOptions.Singleline).Value);
        Assert.All(factory.Realms.Calls, call => Assert.Equal(HttpMethod.Get, call.Method));
        AdminFactory.NoSecrets(html);
    }

    [Theory]
    [InlineData("/Realms/another-realm")]
    [InlineData("/?realmId=another-realm")]
    public async Task DedicatedHostCannotReadAnotherRealm(string path)
    {
        await using var factory = Factory();
        using var browser = factory.Browser();
        browser.BaseAddress = new Uri(Central);
        await AdminFactory.Login(browser);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync(Dedicated + path)).StatusCode);
        Assert.Empty(factory.Realms.Calls);
    }

    [Fact]
    public async Task RealmMutationRetainsAntiforgeryAndRejectsFormRealmOverride()
    {
        await using var factory = Factory();
        using var browser = factory.Browser();
        browser.BaseAddress = new Uri(Central);
        await AdminFactory.Login(browser);
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostAsync(Dedicated + "/?handler=Deploy", AdminFactory.Form(new()))).StatusCode);
        var token = await AdminFactory.Token(browser, Dedicated + "/");
        factory.Realms.Calls.Clear();
        using var tampered = await browser.PostAsync(Dedicated + "/?handler=Deploy", AdminFactory.Form(new()
        { ["__RequestVerificationToken"] = token, ["realmId"] = "another-realm", ["ExpectedRevision"] = "1" }));
        Assert.Equal(HttpStatusCode.NotFound, tampered.StatusCode);
        Assert.Empty(factory.Realms.Calls);
        factory.Realms.Realm = factory.Realms.Realm with { Status = "Draft" };
        using var valid = await browser.PostAsync(Dedicated + "/?handler=Deploy", AdminFactory.Form(new()
        { ["__RequestVerificationToken"] = token, ["ExpectedRevision"] = "1" }));
        Assert.Equal(HttpStatusCode.Redirect, valid.StatusCode);
        Assert.Contains(factory.Realms.Calls, call => call.Method == HttpMethod.Post && call.Path == "/admin/realms/terres-aube/deploy");
    }

    [Fact]
    public async Task GlobalPagesAndLoginGoToCentralHostAndGlobalPostsAreRejected()
    {
        await using var factory = Factory();
        using var browser = factory.Browser();
        using var global = await browser.GetAsync(Dedicated + "/Accounts?search=alice");
        Assert.Equal(Central + "/Accounts?search=alice", global.Headers.Location!.AbsoluteUri);
        using var login = await browser.GetAsync(Dedicated + "/Login");
        Assert.Equal(Central, login.Headers.Location!.GetLeftPart(UriPartial.Authority));
        Assert.Equal(HttpStatusCode.NotFound, (await browser.PostAsync(Dedicated + "/Login?handler=Discord", AdminFactory.Form(new()))).StatusCode);
        Assert.Empty(factory.Realms.Calls);
    }

    [Fact]
    public async Task DraftRealmDoesNotOfferAPublicPage()
    {
        await using var factory = Factory();
        factory.Realms.Realm = factory.Realms.Realm with { Status = "Draft" };
        using var browser = factory.Browser();
        browser.BaseAddress = new Uri(Central);
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync(Dedicated + "/");
        Assert.DoesNotContain("https://terres-aube.arkya.online/", html);
        Assert.Contains("terres-aube", html);
    }

    [Fact]
    public async Task DiscordLoginReturnsToRealmUsingOnlyTheCentralCorrelationCookie()
    {
        await using var factory = Factory();
        factory.Settings["Admin:DiscordEnabled"] = "true";
        factory.Settings["Admin:Password"] = "";
        factory.Settings["Admin:AllowedDiscordUserIds:0"] = DiscordStub.AllowedId;
        factory.Settings["Discord:ClientId"] = DiscordStub.ClientId;
        factory.Settings["Discord:ClientSecret"] = DiscordStub.ClientSecret;
        factory.Settings["Discord:PublicBaseUrl"] = Central;
        factory.Discord.ExpectedCallbackUrl = Central + "/signin-discord";
        using var browser = factory.Browser();
        browser.BaseAddress = new Uri(Central);
        var token = await AdminFactory.Token(browser, "/Login");
        using var challenge = await browser.PostAsync("/Login?handler=Discord", AdminFactory.Form(new()
        {
            ["__RequestVerificationToken"] = token, ["ReturnUrl"] = "/Realms/terres-aube?section=settings"
        }));
        Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
        var query = QueryHelpers.ParseQuery(challenge.Headers.Location!.Query);
        Assert.Equal(Central + "/signin-discord", query["redirect_uri"].ToString());
        var correlation = Assert.Single(challenge.Headers.GetValues("Set-Cookie"), value => value.StartsWith(".AspNetCore.Correlation."));
        Assert.DoesNotContain("domain=", correlation);
        using var callback = await browser.GetAsync(QueryHelpers.AddQueryString("/signin-discord", new Dictionary<string, string?>
        { ["code"] = "discord-code", ["state"] = query["state"].ToString() }));
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal("/Realms/terres-aube?section=settings", callback.Headers.Location!.OriginalString);
        using var realmRedirect = await browser.GetAsync(callback.Headers.Location);
        Assert.Equal(Dedicated + "/?section=settings", realmRedirect.Headers.Location!.AbsoluteUri);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync(realmRedirect.Headers.Location)).StatusCode);
        using var alreadyAuthenticated = await browser.GetAsync("/Login?ReturnUrl=%2FRealms%2Fterres-aube");
        Assert.Equal("/Realms/terres-aube", alreadyAuthenticated.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task LogoutFromRealmRevokesCentralSession()
    {
        await using var factory = Factory();
        using var browser = factory.Browser();
        browser.BaseAddress = new Uri(Central);
        await AdminFactory.Login(browser);
        var token = await AdminFactory.Token(browser, Dedicated + "/");
        using var logout = await browser.PostAsync(Dedicated + "/Logout", AdminFactory.Form(new() { ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.Empty(factory.Sessions.All);
        Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync(Central + "/Realms")).StatusCode);
    }
}
