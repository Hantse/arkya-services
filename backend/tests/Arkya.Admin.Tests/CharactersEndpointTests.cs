using System.Net;
using Microsoft.AspNetCore.WebUtilities;

namespace Arkya.Admin.Tests;

public sealed class CharactersEndpointTests
{
    [Theory]
    [InlineData("Characters", "false")]
    [InlineData("Players", "true")]
    public async Task DirectoriesForwardOwnershipSearchPaginationAndPresenceFilter(string page, string online)
    {
        await using var factory = new AccountsFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync($"/{page}?search=Ali&accountId={AccountsStub.Id:D}&pageNumber=2&pageSize=25");
        var call = Assert.Single(factory.Upstream.Calls);
        var query = QueryHelpers.ParseQuery(call.Uri.Query);
        Assert.Equal("Ali", query["search"].ToString());
        Assert.Equal(AccountsStub.Id.ToString("D"), query["accountId"].ToString());
        Assert.Equal("2", query["page"].ToString());
        Assert.Equal(online, query["onlineOnly"].ToString());
        Assert.Contains("pageNumber=3", html);
        Assert.Contains("&lt;script&gt;character&lt;/script&gt;", html);
        Assert.DoesNotContain("<script>character", html);
        if (page == "Players")
        {
            Assert.Contains("online character(s)", html);
            Assert.DoesNotContain("en ligne", html);
        }
    }

    [Theory]
    [InlineData("Accounts?pageNumber=-1")]
    [InlineData("Accounts?pageSize=101")]
    [InlineData("Characters?accountId=wrong-account")]
    [InlineData("Players?pageNumber=invalid")]
    public async Task InvalidDirectoryFiltersDoNotReachTheServices(string path)
    {
        await using var factory = new AccountsFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        Assert.Contains("validation-summary-errors", await browser.GetStringAsync("/" + path));
        Assert.Empty(factory.Upstream.Calls);
    }

    [Fact]
    public async Task AccountDetailLoadsOnlyOwnedCharactersAndLinksToFilteredDirectory()
    {
        await using var factory = new AccountsFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync($"/Account/{AccountsStub.Id:D}");
        var call = Assert.Single(factory.Upstream.Calls, call => call.Uri.AbsolutePath == "/admin/characters");
        Assert.Equal(AccountsStub.Id.ToString("D"), QueryHelpers.ParseQuery(call.Uri.Query)["accountId"].ToString());
        Assert.Contains("accountId=" + AccountsStub.Id.ToString("D"), html);
        Assert.Contains("&lt;script&gt;character&lt;/script&gt;", html);
    }
}
