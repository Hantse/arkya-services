using System.Net;

namespace Arkya.Admin.Tests;

public sealed class EnglishInterfaceTests
{
    [Theory]
    [InlineData("/", "Services, worlds and player activity.")]
    [InlineData("/Quests", "Write objectives. Publish the next adventures.")]
    [InlineData("/Items", "Manage equipment, Unity models and mob drops")]
    [InlineData("/Item", "Define its properties, appearance and mob loot sources")]
    [InlineData("/Publications", "News and releases")]
    [InlineData("/Publication", "Write, review, then choose when to publish.")]
    [InlineData("/BugReports", "Bugs and suggestions")]
    [InlineData("/BugReport?id=3352f8c9-0cb6-453c-bf92-e2db0ff4fcfa", "Player description")]
    [InlineData("/ReportsConnection", "Connect Codex")]
    [InlineData("/Accounts", "Account management is disabled")]
    [InlineData("/Characters", "Character management is disabled")]
    [InlineData("/Players", "Player presence is unavailable")]
    [InlineData("/Inference", "Inference management is disabled")]
    [InlineData("/Infrastructure", "Deploy a realm")]
    [InlineData("/Realms", "Find your worlds")]
    [InlineData("/CreateRealm", "Configure Areas, instances and raids")]
    [InlineData("/Realms/terres-aube", "Ready servers")]
    [InlineData("/Realms/terres-aube?section=settings", "In-game configuration")]
    [InlineData("/Realms/terres-aube?section=maintenance", "Deploy this realm")]
    [InlineData("/Realms/terres-aube?section=players", "Realm players")]
    [InlineData("/Realms/terres-aube?section=game", "In-game actions")]
    [InlineData("/Realms/terres-aube?section=history", "Realm history")]
    public async Task EveryAdminModuleUsesTheEnglishShellAndCopy(string path, string expected)
    {
        await using var factory = new AdminFactory();
        factory.Settings["RealmsApi:Enabled"] = "true";
        factory.Settings["ReportsApi:Enabled"] = "true";
        using var browser = factory.Browser();
        browser.DefaultRequestHeaders.AcceptLanguage.ParseAdd("fr-FR");
        await AdminFactory.Login(browser);
        var html = WebUtility.HtmlDecode(await browser.GetStringAsync(path));
        Assert.Contains("<html lang=\"en\">", html);
        Assert.Contains("Skip to content", html);
        Assert.Contains("Operator access", html);
        Assert.Contains("Sign out", html);
        Assert.Contains(expected, html);
        AdminFactory.NoSecrets(html);
    }

    [Fact]
    public async Task UiLanguageDoesNotTranslateOperatorOrPlayerContent()
    {
        await using var factory = new AdminFactory();
        factory.Settings["ReportsApi:Enabled"] = "true";
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var quest = WebUtility.HtmlDecode(await browser.GetStringAsync("/Quests"));
        Assert.Contains("La menace du bois", quest);
        Assert.Contains("Vaincre les ennemis.", quest);
        var publication = WebUtility.HtmlDecode(await browser.GetStringAsync("/Publication/" + ContentStub.PostId));
        Assert.Contains("Une île à découvrir", publication);
        Assert.Contains("Les nouveautés de la bêta.", publication);
        Assert.Contains("Exploration et connexion.", publication);
        Assert.Contains("value=\"fr\"", publication);
        Assert.Contains("French", publication);
        var report = WebUtility.HtmlDecode(await browser.GetStringAsync("/BugReport?id=" + ReportsStub.Id));
        Assert.Contains("Bloqué dans une souche", report);
        Assert.Contains("Le personnage ne peut plus avancer.", report);
        Assert.Contains("Près du rivage.", report);
    }

    [Fact]
    public async Task BrowserLanguageAndCultureParametersCannotChangeEnglishFormatting()
    {
        await using var factory = new AccountsFactory();
        factory.Upstream.Total = 12345;
        using var browser = factory.Browser();
        browser.DefaultRequestHeaders.AcceptLanguage.ParseAdd("fr-FR");
        await AdminFactory.Login(browser);
        var html = WebUtility.HtmlDecode(await browser.GetStringAsync("/?culture=fr-FR&ui-culture=fr-FR"));
        Assert.Contains("<html lang=\"en\">", html);
        Assert.Contains("<strong>12,345</strong>", html);
        Assert.Contains("Accounts and access", html);
        var account = WebUtility.HtmlDecode(await browser.GetStringAsync("/Account/" + AccountsStub.Id));
        Assert.Contains("Created UTC", account);
        Assert.Contains("20/10/2026 10:00", account);
    }
}
