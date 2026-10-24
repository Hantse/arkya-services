using System.Net;
using System.Text.RegularExpressions;
using Arkya.Admin.Realms;

namespace Arkya.Admin.Tests;

public sealed class RealmServerVersionsEndpointTests
{
    private const string Path = "/Realms/terres-aube";
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";
    private static readonly DateTimeOffset Seen = new(2026, 10, 24, 13, 15, 40, TimeSpan.Zero);

    private static AdminFactory Factory()
    {
        var factory = new AdminFactory();
        factory.Settings["RealmsApi:Enabled"] = "true";
        factory.Realms.Realm = factory.Realms.Realm with { Status = "Ready", Revision = 4 };
        return factory;
    }

    [Theory]
    [InlineData("overview")]
    [InlineData("maintenance")]
    public async Task ReportedBuildsShowSourceLinksAndRemainDistinctFromTheConfiguredImage(string section)
    {
        await using var factory = Factory();
        factory.Realms.ServerVersions = [new("area-one", Seen, true, "0.1.45", Commit, "123456789"),
            new("area-two", Seen, true, "0.1.45", Commit, "123456789")];
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync(Path + "?section=" + section);
        Assert.Contains("Server versions", html);
        Assert.Contains("0.1.45", html);
        Assert.Contains("href=\"https://github.com/Hantse/arkya-mmorpg/commit/" + Commit + "\"", html);
        Assert.Contains("href=\"https://github.com/Hantse/arkya-mmorpg/actions/runs/123456789\"", html);
        Assert.Contains("0123456789ab</a>", html);
        Assert.Contains("Reporting", html);
        Assert.Contains("24/10/2026 13:15:40 UTC", html);
        Assert.Contains("Configured deployment image", html);
        Assert.Contains(factory.Realms.Realm.Image, html);
        Assert.DoesNotContain("Mixed reported builds", html);
        Assert.Contains("<caption class=\"sr-only\">Last reported build", html);
        AdminFactory.NoSecrets(html);
    }

    [Fact]
    public async Task OlderDedicatedsAndMissingHeartbeatsShowUnknownInsteadOfInferringTheImageVersion()
    {
        await using var factory = Factory();
        factory.Realms.ServerVersions = [new("area-one", Seen, true, null, null, null)];
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync(Path);
        var rows = VersionRows(html);
        Assert.Equal(2, rows.Length);
        Assert.All(rows, row => Assert.Contains(">Unknown</strong>", row));
        Assert.Contains("Reporting", rows[0]);
        Assert.Contains("Not reported", rows[1]);
        Assert.Contains("No heartbeat received", rows[1]);
        Assert.DoesNotContain(factory.Realms.Realm.Image, string.Join("", rows));
        Assert.DoesNotContain("/commit/", string.Join("", rows));
        Assert.DoesNotContain("/actions/runs/", string.Join("", rows));
    }

    [Fact]
    public async Task StaleReportsRetainTheirIdentityWithAnExplicitOfflineStateAndMixedBuildNotice()
    {
        await using var factory = Factory();
        factory.Realms.ServerVersions = [new("area-one", Seen, false, "0.1.44", new string('a', 40), "123456788"),
            new("area-two", Seen.AddMinutes(1), true, "0.1.45", Commit, "123456789")];
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync(Path);
        var rows = VersionRows(html);
        Assert.Contains("Mixed reported builds", html);
        Assert.Contains("stale report does not confirm the running version", html);
        Assert.Contains("0.1.44", rows[0]);
        Assert.Contains("Stale / offline", rows[0]);
        Assert.DoesNotContain(">Reporting<", rows[0]);
        Assert.Contains(">Reporting<", rows[1]);
    }

    [Theory]
    [InlineData("overview", HttpStatusCode.ServiceUnavailable)]
    [InlineData("maintenance", HttpStatusCode.ServiceUnavailable)]
    [InlineData("overview", HttpStatusCode.NotFound)]
    [InlineData("maintenance", HttpStatusCode.NotFound)]
    public async Task VersionEndpointFailurePreservesTheSelectedPageAndItsOtherCapabilities(string section, HttpStatusCode status)
    {
        await using var factory = Factory();
        factory.Realms.ServerVersionsReadError = status;
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        using var response = await browser.GetAsync(Path + "?section=" + section);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Server versions are unavailable", html);
        Assert.Contains("Configured deployment image", html);
        if (section == "maintenance") Assert.Contains("handler=Update", html);
        else Assert.Contains("Ready servers", html);
        Assert.DoesNotContain("No build version reported", html);
        AdminFactory.NoSecrets(html);
    }

    [Fact]
    public async Task MalformedVersionResponseIsUnavailableAndSourceReferencesCannotCreateArbitraryLinks()
    {
        await using var factory = Factory();
        factory.Realms.ServerVersionsInvalidJson = true;
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        Assert.Contains("Server versions are unavailable", await browser.GetStringAsync(Path));
        factory.Realms.ServerVersionsInvalidJson = false;
        factory.Realms.ServerVersions = [new("area-one", Seen, true, "<script>version</script>", "../evil", "123\" onclick=\"evil")];
        var html = await browser.GetStringAsync(Path);
        Assert.DoesNotContain("<script>version", html);
        Assert.Contains("&lt;script&gt;version&lt;/script&gt;", html);
        Assert.Contains("Commit unknown", html);
        Assert.DoesNotContain("/commit/", html);
        Assert.DoesNotContain("/actions/runs/", html);
        Assert.DoesNotContain("onclick=\"evil", html);
        AdminFactory.NoSecrets(html);
    }

    private static string[] VersionRows(string html)
    {
        var table = Regex.Match(html, "<table class=\"realm-table realm-version-table\".*?</table>", RegexOptions.Singleline).Value;
        return Regex.Matches(table, "<tr>.*?</tr>", RegexOptions.Singleline).Skip(1).Select(match => match.Value).ToArray();
    }
}
