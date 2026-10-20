using System.Net;
using Arkya.Admin.Pages;
using Arkya.ServiceDefaults;

namespace Arkya.Admin.Tests;

public sealed class ReportsConnectionTests
{
    private const string PagePath = "/ReportsConnection";
    private const string DownloadPath = PagePath + "?handler=Download";

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    public async Task AnonymousRequestsCannotRetrieveTheConnectionKey(string method)
    {
        await using var factory = EnabledFactory();
        using var browser = factory.Browser();
        using var response = await browser.SendAsync(new HttpRequestMessage(new HttpMethod(method), DownloadPath));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Login?", response.Headers.Location!.OriginalString);
        AdminFactory.NoSecrets(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GetOnlyDisplaysSetupAndNeverExportsTheKeyIncludingWithDownloadHandler()
    {
        await using var factory = EnabledFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        using var response = await browser.GetAsync(DownloadPath);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains(ReportsConnectionModel.McpUrl, html);
        Assert.Contains("handler=Download", html);
        Assert.Null(response.Content.Headers.ContentDisposition);
        Assert.True(response.Headers.CacheControl!.NoStore);
        AdminFactory.NoSecrets(html);
    }

    [Fact]
    public async Task AuthenticatedDownloadRequiresAntiforgery()
    {
        await using var factory = EnabledFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        using var response = await browser.PostAsync(DownloadPath, AdminFactory.Form([]));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AdminFactory.NoSecrets(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task DisabledConnectionHasNoExportEvenWithAValidCookieAndAntiforgeryToken()
    {
        await using var factory = new AdminFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync(PagePath);
        Assert.Contains("connexion MCP est désactivée", WebUtility.HtmlDecode(html));
        Assert.DoesNotContain("handler=Download", html);
        AdminFactory.NoSecrets(html);
        using var response = await browser.PostAsync(DownloadPath, AdminFactory.Form(new()
        {
            ["__RequestVerificationToken"] = AdminFactory.Input(html, "__RequestVerificationToken")
        }));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AdminFactory.NoSecrets(await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitExportContainsOnlyTheMcpCredentialAndPublicUrl(bool deriveKeys)
    {
        await using var factory = EnabledFactory();
        var key = AdminFactory.McpKey;
        var internalKey = AdminFactory.ReportsAdminKey;
        if (deriveKeys)
        {
            factory.Settings["ReportsOperator:KeySeed"] = ReportsOperatorKeyTests.Seed;
            factory.Settings["ReportsApi:AdminKey"] = "";
            factory.Settings["AdminMcp:Key"] = "";
            key = ReportsOperatorKeys.ResolveMcp(ReportsOperatorKeyTests.Configuration(), "");
            internalKey = ReportsOperatorKeys.ResolveReports(ReportsOperatorKeyTests.Configuration(), "");
        }
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        using var response = await browser.PostAsync(DownloadPath, AdminFactory.Form(new()
        {
            ["__RequestVerificationToken"] = await AdminFactory.Token(browser, PagePath)
        }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/toml", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Contains("arkya-admin-mcp.local.toml", response.Content.Headers.ContentDisposition.ToString());
        Assert.True(response.Headers.CacheControl!.NoStore);
        var configuration = await response.Content.ReadAsStringAsync();
        Assert.Equal($"[mcp_servers.arkya_admin]\nurl = \"{ReportsConnectionModel.McpUrl}\"\n\n" +
            $"[mcp_servers.arkya_admin.http_headers]\nAuthorization = \"Bearer {key}\"\n", configuration);
        Assert.DoesNotContain(internalKey, configuration);
        Assert.DoesNotContain(ReportsOperatorKeyTests.Seed, configuration);
        Assert.DoesNotContain(AdminFactory.AdminKey, configuration);
        Assert.DoesNotContain(AdminFactory.Password, configuration);
        Assert.DoesNotContain("reports.example", configuration);
        Assert.Empty(factory.Reports.Calls);
    }

    private static AdminFactory EnabledFactory()
    {
        var factory = new AdminFactory();
        factory.Settings["ReportsApi:Enabled"] = "true";
        factory.Settings["AdminMcp:Enabled"] = "true";
        return factory;
    }
}
