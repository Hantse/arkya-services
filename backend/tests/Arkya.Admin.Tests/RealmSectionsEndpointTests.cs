using System.Net;
using System.Text.RegularExpressions;

namespace Arkya.Admin.Tests;

public sealed class RealmSectionsEndpointTests
{
    private const string Path = "/Realms/terres-aube";
    private const string ApiPath = "/admin/realms/terres-aube";
    private static readonly string[] Sections = ["overview", "players", "settings", "maintenance", "game", "history"];
    private static readonly string[] MutationHandlers = ["Deploy", "Configuration", "Restart", "Update", "Delete", "Announce", "Command"];

    private static AdminFactory Factory(bool enabled = true)
    {
        var factory = new AdminFactory();
        factory.Settings["RealmsApi:Enabled"] = enabled ? "true" : "false";
        factory.Realms.Realm = factory.Realms.Realm with { Status = "Ready", Revision = 4 };
        return factory;
    }

    [Theory]
    [InlineData("overview")]
    [InlineData("players")]
    [InlineData("settings")]
    [InlineData("maintenance")]
    [InlineData("game")]
    [InlineData("history")]
    public async Task SectionNavigationRendersOnlySelectedContentAndPreservesItWhenRefreshing(string section)
    {
        await using var factory = Factory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync(Path + "?section=" + section);
        Assert.Contains("data-realm-section=\"" + section + "\"", html);
        foreach (var other in Sections.Where(value => value != section))
            Assert.DoesNotContain("data-realm-section=\"" + other + "\"", html);
        var navigation = Navigation(html);
        var sectionLinks = Regex.Matches(navigation, @"<a\b[^>]*href=""[^""]*\?section=[^""]*""[^>]*>.*?</a>", RegexOptions.Singleline);
        Assert.Equal(Sections.Length, sectionLinks.Count);
        foreach (var destination in Sections)
            Assert.Single(sectionLinks.Cast<Match>(), link => link.Value.Contains("href=\"" + Path + "?section=" + destination + "\""));
        Assert.DoesNotContain("<nav", Regex.Match(html, @"<main\b.*?</main>", RegexOptions.Singleline).Value);
        Assert.DoesNotContain("Accounts et acc", navigation);
        Assert.Contains("href=\"/Realms\"", navigation);
        Assert.Contains("General administration", WebUtility.HtmlDecode(navigation));
        var anchors = Regex.Matches(html, @"<a\b[^>]*>.*?</a>", RegexOptions.Singleline).Select(match => match.Value).ToArray();
        var active = Assert.Single(anchors, anchor => anchor.Contains("aria-current=\"page\"") && anchor.Contains("?section="));
        Assert.Contains("href=\"" + Path + "?section=" + section + "\"", active);
        var refresh = Assert.Single(anchors, anchor => anchor.Contains("Refresh"));
        Assert.Contains("href=\"" + Path + "?section=" + section + "\"", refresh);
        var actionPanels = Regex.Matches(html, @"<details\b[^>]*class=""[^""]*realm-action-disclosure[^""]*""[^>]*>");
        Assert.All(actionPanels.Cast<Match>(), panel => Assert.DoesNotMatch(@"\sopen(?:\s|=|>)", panel.Value));
        var visibleHandlers = section switch
        {
            "settings" => new[] { "Configuration" },
            "maintenance" => ["Restart", "Update", "Delete"],
            "game" => ["Announce", "Command"],
            _ => []
        };
        foreach (var handler in MutationHandlers)
        {
            if (visibleHandlers.Contains(handler)) Assert.Contains("handler=" + handler, html);
            else Assert.DoesNotContain("handler=" + handler, html);
        }
        Assert.All(factory.Realms.Calls, call => Assert.Equal(HttpMethod.Get, call.Method));
        AdminFactory.NoSecrets(html);
    }

    [Theory]
    [InlineData("overview")]
    [InlineData("players")]
    [InlineData("settings")]
    [InlineData("maintenance")]
    [InlineData("game")]
    [InlineData("history")]
    public async Task SectionLoadsOnlyTheDependenciesItDisplays(string section)
    {
        await using var factory = Factory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        await browser.GetStringAsync(Path + "?section=" + section);
        string[] dependencies = section switch
        {
            "settings" or "history" => [ApiPath, ApiPath + "/commands", ApiPath + "/configuration"],
            "maintenance" => [ApiPath, ApiPath + "/server-versions", "/admin/realms/options", ApiPath + "/operations-options"],
            "overview" => [ApiPath, ApiPath + "/server-versions"],
            "game" => [ApiPath, ApiPath + "/commands"],
            _ => [ApiPath]
        };
        Assert.Equal(dependencies.Order(), factory.Realms.Calls.Select(call => call.Path).Order());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData("../Shared/_Layout")]
    [InlineData("<script>unsafe()</script>")]
    public async Task MissingOrUnknownSectionsUseTheCompactReadOnlyOverview(string? section)
    {
        await using var factory = Factory();
        factory.Realms.ConfigurationReadError = HttpStatusCode.ServiceUnavailable;
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var url = section is null ? Path : Path + "?section=" + Uri.EscapeDataString(section);
        var html = await browser.GetStringAsync(url);
        Assert.Contains("data-realm-section=\"overview\"", html);
        Assert.Contains("?section=overview\" aria-current=\"page\"", Navigation(html));
        Assert.DoesNotContain("<script>unsafe", html);
        Assert.DoesNotContain("In-game configuration is unavailable", WebUtility.HtmlDecode(html));
        Assert.Contains("area-one", html);
        foreach (var handler in MutationHandlers) Assert.DoesNotContain("handler=" + handler, html);
        Assert.Equal(new[] { ApiPath, ApiPath + "/server-versions" }, factory.Realms.Calls.Select(call => call.Path));
        AdminFactory.NoSecrets(html);
    }

    [Theory]
    [InlineData("overview")]
    [InlineData("players")]
    [InlineData("settings")]
    [InlineData("maintenance")]
    [InlineData("game")]
    [InlineData("history")]
    public async Task EverySectionKeepsAuthenticationAndFeatureAvailabilityChecks(string section)
    {
        await using var enabled = Factory();
        using var anonymous = enabled.Browser();
        Assert.Equal(HttpStatusCode.Redirect, (await anonymous.GetAsync(Path + "?section=" + section)).StatusCode);
        Assert.Empty(enabled.Realms.Calls);
        await using var disabled = Factory(enabled: false);
        using var browser = disabled.Browser();
        await AdminFactory.Login(browser);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync(Path + "?section=" + section)).StatusCode);
        Assert.Empty(disabled.Realms.Calls);
    }

    [Theory]
    [InlineData("Configuration", "settings")]
    [InlineData("Restart", "maintenance")]
    [InlineData("Update", "maintenance")]
    [InlineData("Delete", "maintenance")]
    [InlineData("Announce", "game")]
    [InlineData("Command", "game")]
    public async Task ValidationReturnsToTheHandlerSectionEvenWhenTheSubmittedSectionIsDifferent(string handler, string section)
    {
        await using var factory = Factory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync(Path + "?section=" + section);
        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = AdminFactory.Input(html, "__RequestVerificationToken"),
            ["Reason"] = "", ["Section"] = "overview"
        };
        if (handler == "Configuration")
        {
            form["ConfigurationRevision"] = AdminFactory.Input(html, "ConfigurationRevision");
            form["XpMultiplier"] = "2";
            form["HarvestMultiplier"] = "3";
        }
        else
        {
            form["ExpectedRevision"] = AdminFactory.Input(html, "ExpectedRevision");
            if (handler is "Restart" or "Update" or "Delete")
            {
                form["ConfirmRealmId"] = "terres-aube";
                if (handler == "Update") form["ExpectedImage"] = AdminFactory.Input(html, "ExpectedImage");
            }
            else
            {
                form["RequestId"] = AdminFactory.Input(html, "RequestId");
                form["ServerId"] = "area-one";
                form["Message"] = "Bonjour";
                form["CommandType"] = "save";
            }
        }
        using var response = await browser.PostAsync(Path + "?handler=" + handler + "&section=history", AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadAsStringAsync();
        Assert.Contains("data-realm-section=\"" + section + "\"", result);
        Assert.Contains("?section=" + section + "\" aria-current=\"page\"", Navigation(result));
        Assert.Contains("validation-summary-errors", result);
        if (handler != "Configuration")
        {
            var panels = Regex.Matches(result, @"<details\b[^>]*class=""[^""]*realm-action-disclosure[^""]*""[^>]*>.*?</details>", RegexOptions.Singleline);
            var opened = Assert.Single(panels.Cast<Match>(), panel => Regex.IsMatch(panel.Value[..(panel.Value.IndexOf('>') + 1)], @"\sopen(?:\s|=|>)"));
            Assert.Contains("handler=" + handler, opened.Value);
        }
        Assert.DoesNotContain(factory.Realms.Calls, call => call.Method != HttpMethod.Get);
        AdminFactory.NoSecrets(result);
    }

    [Fact]
    public async Task HistoryCombinesTheThreeAuditStreamsWithoutMutationControls()
    {
        await using var factory = Factory();
        var now = DateTimeOffset.UtcNow;
        factory.Realms.Realm = factory.Realms.Realm with { Operations = [new("Restart", 4, "admin:operator", "operation-audit-entry", now,
            now, "Ready", null, "image-a", "image-a")] };
        factory.Realms.Configuration = new(1, 2, 3, now, "admin:operator", [new(1, 0, 2, 3, 1, 1, now, "admin:operator", "configuration-audit-entry")]);
        factory.Realms.Commands = factory.Realms.Commands with { Commands = [new(Guid.NewGuid().ToString(), "area-one", "announce",
            "Announcement", "", "Succeeded", "admin:operator", "command-audit-entry", now, now.AddMinutes(2), now, "announced", 2)] };
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var history = await browser.GetStringAsync(Path + "?section=history");
        foreach (var entry in new[] { "operation-audit-entry", "configuration-audit-entry", "command-audit-entry" })
            Assert.Contains(entry, history);
        foreach (var handler in MutationHandlers) Assert.DoesNotContain("handler=" + handler, history);
        foreach (var section in Sections.Where(section => section != "history"))
        {
            var html = await browser.GetStringAsync(Path + "?section=" + section);
            foreach (var entry in new[] { "operation-audit-entry", "configuration-audit-entry", "command-audit-entry" })
                Assert.DoesNotContain(entry, html);
        }
    }

    [Theory]
    [InlineData("/Realms")]
    [InlineData("/CreateRealm")]
    public async Task CatalogueAndCreationKeepTheGeneralAdminNavigation(string path)
    {
        await using var factory = Factory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var navigation = Navigation(await browser.GetStringAsync(path));
        Assert.Contains("href=\"/\"", navigation);
        Assert.Contains("href=\"/Accounts\"", navigation);
        Assert.Contains("href=\"/Realms\"", navigation);
        Assert.DoesNotContain("?section=", navigation);
        Assert.All(factory.Realms.Calls, call => Assert.Equal(HttpMethod.Get, call.Method));
    }

    private static string Navigation(string html) => Regex.Match(html,
        @"<nav\b[^>]*aria-label=""Main menu""[^>]*>.*?</nav>", RegexOptions.Singleline).Value;
}
