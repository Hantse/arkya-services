using System.Net;
using System.Text.Json;
using Arkya.ServiceDefaults.Accounts;

namespace Arkya.Admin.Tests;

internal sealed partial class AccountsStub
{
    public BetaRegistrationSummary BetaRegistration { get; set; } = new(Id.ToString("D"), "<script>player</script>",
        "123456789012345678", DateTimeOffset.Parse("2026-10-24T10:00:00Z"), false, false, 1);

    private HttpResponseMessage HandleBeta(HttpMethod method, Uri uri, JsonElement? body, string? actor)
    {
        if (method == HttpMethod.Get)
        {
            var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);
            return Json(new BetaRegistrationPage(Empty ? [] : [BetaRegistration], 1,
                int.Parse(query["page"].ToString()), int.Parse(query["pageSize"].ToString())));
        }
        Assert.Equal("admin:operator", actor);
        if (uri.AbsolutePath == "/admin/beta/grants/random")
        {
            var request = body!.Value.Deserialize<BetaRandomGrantRequest>(JsonSerializerOptions.Web)!;
            return Json(new BetaRandomGrantResponse(request.RequestId, request.Count, 1, [Id.ToString("D")], DateTimeOffset.UtcNow));
        }
        var grant = body!.Value.Deserialize<BetaGrantRequest>(JsonSerializerOptions.Web)!;
        if (grant.ExpectedRevision != BetaRegistration.Revision) return new(HttpStatusCode.Conflict);
        BetaRegistration = BetaRegistration with { HasBetaAccess = true, Revision = grant.ExpectedRevision + 1 };
        return Json(BetaRegistration);
    }
}

public sealed class BetaRegistrationTests
{
    [Theory]
    [InlineData("/BetaRegistrations", "GET")]
    [InlineData("/BetaRegistrations?handler=Grant", "POST")]
    [InlineData("/BetaRegistrations?handler=Random", "POST")]
    public async Task RegistrationManagementRequiresOperatorLogin(string path, string method)
    {
        await using var factory = new AccountsFactory();
        using var browser = factory.Browser();
        using var response = await browser.SendAsync(new(new HttpMethod(method), path));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Empty(factory.Upstream.Calls);
    }

    [Theory]
    [InlineData("Grant")]
    [InlineData("Random")]
    public async Task AccessGrantsRequireAntiforgery(string handler)
    {
        await using var factory = new AccountsFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        using var response = await browser.PostAsync("/BetaRegistrations?handler=" + handler, AdminFactory.Form([]));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.Upstream.Calls);
    }

    [Fact]
    public async Task WaitingListHasEscapedPlayersFiltersAndRandomAccessDialog()
    {
        await using var factory = new AccountsFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        using var response = await browser.GetAsync("/BetaRegistrations?search=Aelys&status=pending&pageNumber=2&pageSize=50&draw=true");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("script-src 'self'", Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
        Assert.Contains("&lt;script&gt;player&lt;/script&gt;", html);
        Assert.DoesNotContain("<script>player</script>", html);
        Assert.Contains("Give x random access", html);
        Assert.Contains("<dialog", html);
        Assert.Contains("name=\"RequestId\"", html);
        Assert.Contains("max=\"100\"", html);
        var query = Assert.Single(factory.Upstream.Calls).Uri.Query;
        Assert.Contains("status=pending", query);
        Assert.Contains("search=Aelys", query);
        Assert.Contains("page=2", query);
        Assert.Contains("pageSize=50", query);
        AdminFactory.NoSecrets(html);
        Assert.DoesNotContain(AccountsStub.Key, html);
    }

    [Fact]
    public async Task ManualGrantUsesObservedRevisionAndServerOwnedActor()
    {
        await using var factory = new AccountsFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var token = await AdminFactory.Token(browser, "/BetaRegistrations");
        using var response = await browser.PostAsync("/BetaRegistrations?handler=Grant", AdminFactory.Form(new()
        {
            ["__RequestVerificationToken"] = token, ["AccountId"] = AccountsStub.Id.ToString(),
            ["ExpectedRevision"] = "1", ["GrantReason"] = "Approved tester", ["Actor"] = "forged"
        }));
        Assert.True(response.StatusCode == HttpStatusCode.Redirect, ValidationErrors(await response.Content.ReadAsStringAsync()));
        Assert.True(factory.Upstream.BetaRegistration.HasBetaAccess);
        var write = Assert.Single(factory.Upstream.Calls, call => call.Method == HttpMethod.Post);
        Assert.Equal("admin:operator", write.Actor);
        Assert.Equal(1, write.Body!.Value.GetProperty("expectedRevision").GetInt64());
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(101, false)]
    [InlineData(3, true)]
    public async Task RandomDrawValidatesCountAndForwardsStableRequestId(int count, bool accepted)
    {
        await using var factory = new AccountsFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var token = await AdminFactory.Token(browser, "/BetaRegistrations");
        var requestId = Guid.NewGuid();
        using var response = await browser.PostAsync("/BetaRegistrations?handler=Random", AdminFactory.Form(new()
        {
            ["__RequestVerificationToken"] = token, ["RequestId"] = requestId.ToString(), ["Count"] = count.ToString(),
            ["Reason"] = "October beta wave"
        }));
        Assert.True(response.StatusCode == (accepted ? HttpStatusCode.Redirect : HttpStatusCode.OK), ValidationErrors(await response.Content.ReadAsStringAsync()));
        if (accepted)
        {
            var write = Assert.Single(factory.Upstream.Calls, call => call.Method == HttpMethod.Post);
            Assert.Equal(requestId, write.Body!.Value.GetProperty("requestId").GetGuid());
            Assert.Equal(count, write.Body.Value.GetProperty("count").GetInt32());
            Assert.Equal("admin:operator", write.Actor);
            var html = await browser.GetStringAsync(response.Headers.Location!);
            Assert.Contains("Granted 1 of 3 requested", html);
        }
        else Assert.DoesNotContain(factory.Upstream.Calls, call => call.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task DrawTimeoutPreservesTheOperationIdentityForSafeRetry()
    {
        await using var factory = new AccountsFactory();
        factory.Upstream.WriteError = HttpStatusCode.ServiceUnavailable;
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var token = await AdminFactory.Token(browser, "/BetaRegistrations");
        var requestId = Guid.NewGuid();
        using var response = await browser.PostAsync("/BetaRegistrations?handler=Random", AdminFactory.Form(new()
        {
            ["__RequestVerificationToken"] = token, ["RequestId"] = requestId.ToString(), ["Count"] = "2", ["Reason"] = "Beta wave"
        }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(requestId.ToString(), AdminFactory.Input(html, "RequestId"));
        Assert.DoesNotContain(AccountsStub.Key, html);
        Assert.DoesNotContain("private upstream", html);
    }

    private static string ValidationErrors(string html) => string.Join("; ",
        System.Text.RegularExpressions.Regex.Matches(html, "<li>(.*?)</li>").Select(match => WebUtility.HtmlDecode(match.Groups[1].Value)));
}
