using System.Net;
using Arkya.Admin.Accounts;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Arkya.Admin.Tests;

public sealed class CharactersAdminClientTests
{
    [Fact]
    public async Task ScopedDirectoryForwardsTheRealmAlongsideAllOtherFilters()
    {
        using var upstream = new AccountsStub();
        using var http = new HttpClient(upstream) { BaseAddress = new("https://characters.example/") };
        var client = Client(http);
        var result = await client.ListAsync(AccountsStub.Id.ToString("D"), "Aelys", 2, 10, true, default, "beta");
        Assert.Equal("beta", Assert.Single(result.Items).RealmId);
        var call = Assert.Single(upstream.Calls);
        var query = QueryHelpers.ParseQuery(call.Uri.Query);
        Assert.Equal("beta", query["realmId"].ToString());
        Assert.Equal(AccountsStub.Id.ToString("D"), query["accountId"].ToString());
        Assert.Equal("Aelys", query["search"].ToString());
        Assert.Equal("2", query["page"].ToString());
        Assert.Equal("10", query["pageSize"].ToString());
        Assert.Equal("true", query["onlineOnly"].ToString());
    }

    [Fact]
    public async Task ScopedDirectoryRefusesCharactersFromAnotherRealmIfTheServiceIgnoresTheFilter()
    {
        using var upstream = new AccountsStub();
        using var http = new HttpClient(upstream) { BaseAddress = new("https://characters.example/") };
        var error = await Assert.ThrowsAsync<PlayerAdminApiException>(() =>
            Client(http).ListAsync(null, null, 1, 25, false, default, "lerya"));
        Assert.Equal(HttpStatusCode.BadGateway, error.StatusCode);
        Assert.Contains("another realm", error.Message);
    }

    [Fact]
    public async Task GlobalDirectoryKeepsTheRealmFilterAbsent()
    {
        using var upstream = new AccountsStub();
        using var http = new HttpClient(upstream) { BaseAddress = new("https://characters.example/") };
        Assert.Single((await Client(http).ListAsync(null, null, 1, 25, false, default)).Items);
        Assert.False(QueryHelpers.ParseQuery(Assert.Single(upstream.Calls).Uri.Query).ContainsKey("realmId"));
    }

    private static CharactersAdminClient Client(HttpClient http) => new(http,
        Options.Create(new CharactersAdminApiOptions { Enabled = true, AdminKey = AccountsStub.Key }));
}
