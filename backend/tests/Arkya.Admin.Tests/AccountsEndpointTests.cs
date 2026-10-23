using System.Net;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Arkya.Admin.Tests;

public sealed class AccountsEndpointTests
{
    private static string Detail => $"/Account/{AccountsStub.Id:D}";

    [Fact]
    public async Task AccountPagesRequireAuthenticationAndMutationsRequireAntiforgery()
    {
        await using var factory = new AccountsFactory();
        using var browser = factory.Browser();
        foreach (var path in new[] { "/Accounts", Detail, "/Players", "/Characters" })
            Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync(path)).StatusCode);
        Assert.Empty(factory.Upstream.Calls);
        await AdminFactory.Login(browser);
        using var rejected = await browser.PostAsync(Detail + "?handler=Ban", AdminFactory.Form(new() { ["ExpectedRevision"] = "1", ["Reason"] = "Abus" }));
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Empty(factory.Upstream.Calls);
    }

    [Fact]
    public async Task AccountFiltersPaginationAndUntrustedNamesAreHandled()
    {
        await using var factory = new AccountsFactory();
        factory.Upstream.Account = factory.Upstream.Account with { DisplayName = "<script>account</script>" };
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync("/Accounts?search=%20Aelys%20&pageNumber=2&pageSize=25");
        var call = Assert.Single(factory.Upstream.Calls);
        var query = QueryHelpers.ParseQuery(call.Uri.Query);
        Assert.Equal("Aelys", query["search"].ToString());
        Assert.Equal("2", query["page"].ToString());
        Assert.Contains("&lt;script&gt;account&lt;/script&gt;", html);
        Assert.Contains("pageNumber=3", html);
        Assert.DoesNotContain("<script>account", html);
        Assert.DoesNotContain(AccountsStub.Key, html);
    }

    [Theory]
    [InlineData("Ban")]
    [InlineData("Unban")]
    [InlineData("Beta")]
    public async Task ModerationPostsRevisionReasonAndServerActor(string handler)
    {
        await using var factory = new AccountsFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = Form(await AdminFactory.Token(browser, Detail));
        form["Actor"] = "forged-actor";
        using var response = await browser.PostAsync(Detail + "?handler=" + handler, AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var write = Assert.Single(factory.Upstream.Calls, call => call.Method == HttpMethod.Post);
        Assert.Equal("admin:operator", write.Actor);
        Assert.Equal(1, write.Body!.Value.GetProperty("expectedRevision").GetInt64());
        Assert.Equal("Vérification opérateur", write.Body.Value.GetProperty("reason").GetString());
        if (handler == "Beta") Assert.True(write.Body.Value.GetProperty("hasBetaAccess").GetBoolean());
        Assert.DoesNotContain(AccountsStub.Key, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("ExpectedRevision", "")]
    [InlineData("ExpectedRevision", "-1")]
    [InlineData("Reason", "")]
    [InlineData("Reason", "ligne\ncontrôle")]
    public async Task InvalidModerationDoesNotReachIdentity(string field, string value)
    {
        await using var factory = new AccountsFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = Form(await AdminFactory.Token(browser, Detail));
        form[field] = value;
        using var response = await browser.PostAsync(Detail + "?handler=Ban", AdminFactory.Form(form));
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.BadRequest });
        Assert.DoesNotContain(factory.Upstream.Calls, call => call.Method == HttpMethod.Post);
    }

    [Theory]
    [InlineData("Ban")]
    [InlineData("Beta")]
    public async Task UntouchedAccountAtRevisionZeroCanBeListedLoadedAndUpdated(string handler)
    {
        await using var factory = new AccountsFactory();
        factory.Upstream.Account = factory.Upstream.Account with { Revision = 0 };
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var list = await browser.GetStringAsync("/Accounts");
        Assert.Contains("Aelys", list);
        Assert.DoesNotContain("role=\"alert\"", list);
        var detail = await browser.GetStringAsync(Detail);
        Assert.Equal("0", AdminFactory.Input(detail, "ExpectedRevision"));
        var form = Form(AdminFactory.Input(detail, "__RequestVerificationToken"));
        form["ExpectedRevision"] = "0";
        using var response = await browser.PostAsync(Detail + "?handler=" + handler, AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var write = Assert.Single(factory.Upstream.Calls, call => call.Method == HttpMethod.Post);
        Assert.Equal(0, write.Body!.Value.GetProperty("expectedRevision").GetInt64());
        Assert.Equal(1, factory.Upstream.Account.Revision);
    }

    [Fact]
    public async Task MissingRevisionCannotBeMistakenForRevisionZero()
    {
        await using var factory = new AccountsFactory();
        factory.Upstream.Account = factory.Upstream.Account with { Revision = 0 };
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = Form(await AdminFactory.Token(browser, Detail));
        form.Remove("ExpectedRevision");
        using var response = await browser.PostAsync(Detail + "?handler=Ban", AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(factory.Upstream.Calls, call => call.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task ConflictReloadsCurrentRevisionAndSanitizesUpstreamDetails()
    {
        await using var factory = new AccountsFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = Form(await AdminFactory.Token(browser, Detail));
        factory.Upstream.Account = factory.Upstream.Account with { Revision = 4, HasBetaAccess = true };
        factory.Upstream.WriteError = HttpStatusCode.Conflict;
        using var response = await browser.PostAsync(Detail + "?handler=Beta", AdminFactory.Form(form));
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("The current version has been reloaded", WebUtility.HtmlDecode(html));
        Assert.Equal("4", AdminFactory.Input(html, "ExpectedRevision"));
        Assert.Equal("false", AdminFactory.Input(html, "HasBetaAccess"));
        Assert.DoesNotContain("private upstream details", html);
        Assert.DoesNotContain(AccountsStub.Key, html);
    }

    [Fact]
    public async Task DisabledConnectionsShowConfigurationStatesWithoutNetworkCalls()
    {
        await using var factory = new AccountsFactory();
        factory.Settings["IdentityAdminApi:Enabled"] = "false";
        factory.Settings["CharactersAdminApi:Enabled"] = "false";
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        Assert.Contains("disabled", WebUtility.HtmlDecode(await browser.GetStringAsync("/Accounts")));
        Assert.Contains("disabled", WebUtility.HtmlDecode(await browser.GetStringAsync("/Characters")));
        Assert.Contains("unavailable", await browser.GetStringAsync("/Players"));
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync(Detail)).StatusCode);
        Assert.Empty(factory.Upstream.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DependencyFailuresRemainSafeAndRecoverable(bool invalidJson)
    {
        await using var factory = new AccountsFactory();
        if (invalidJson) factory.Upstream.InvalidJson = true;
        else factory.Upstream.ReadError = HttpStatusCode.ServiceUnavailable;
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        foreach (var path in new[] { "/Accounts", Detail, "/Players", "/Characters" })
        {
            var html = await browser.GetStringAsync(path);
            Assert.Contains("role=\"alert\"", html);
            Assert.DoesNotContain("private upstream details", html);
            Assert.DoesNotContain(AccountsStub.Key, html);
        }
    }

    [Theory]
    [InlineData("IdentityAdminApi", "http://identity.example/")]
    [InlineData("CharactersAdminApi", "https://secret@characters.example/")]
    [InlineData("IdentityAdminApi", "https://identity.example/?key=secret")]
    public void UnsafeDestinationsFailStartup(string section, string url)
    {
        using var factory = new AccountsFactory();
        factory.Settings[section + ":BaseUrl"] = url;
        Assert.Throws<OptionsValidationException>(() => factory.Browser());
    }

    private static Dictionary<string, string> Form(string token) => new()
    {
        ["__RequestVerificationToken"] = token, ["ExpectedRevision"] = "1", ["Reason"] = "Vérification opérateur", ["HasBetaAccess"] = "true"
    };
}
