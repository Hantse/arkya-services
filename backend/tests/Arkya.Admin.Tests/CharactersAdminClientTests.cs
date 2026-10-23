using System.Net;
using Arkya.Admin.Accounts;
using Arkya.ServiceDefaults.Characters;
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

    [Fact]
    public async Task DetailUsesThePrivateEndpointAndForwardsRealmAndQuestPagination()
    {
        using var upstream = new AccountsStub();
        using var http = new HttpClient(upstream) { BaseAddress = new("https://characters.example/") };
        var result = await Client(http).GetAsync(AccountsStub.CharacterId, default, "beta", 3, 10);
        Assert.Equal(AccountsStub.CharacterId.ToString("D"), result.Character.CharacterId);
        Assert.Equal(3, result.Quests.Page);
        Assert.Equal(10, result.Quests.PageSize);
        var call = Assert.Single(upstream.Calls);
        Assert.Equal(HttpMethod.Get, call.Method);
        Assert.Equal($"/admin/characters/{AccountsStub.CharacterId:D}", call.Uri.AbsolutePath);
        var query = QueryHelpers.ParseQuery(call.Uri.Query);
        Assert.Equal("beta", query["realmId"].ToString());
        Assert.Equal("3", query["questPage"].ToString());
        Assert.Equal("10", query["questPageSize"].ToString());
        Assert.Null(call.Actor);
        Assert.Null(call.Body);
    }

    [Fact]
    public async Task GlobalDetailKeepsTheRealmFilterAbsent()
    {
        using var upstream = new AccountsStub();
        using var http = new HttpClient(upstream) { BaseAddress = new("https://characters.example/") };
        await Client(http).GetAsync(AccountsStub.CharacterId, default);
        Assert.False(QueryHelpers.ParseQuery(Assert.Single(upstream.Calls).Uri.Query).ContainsKey("realmId"));
    }

    [Fact]
    public async Task DetailRefusesAnotherRealmEvenIfTheServiceIgnoresTheFilter()
    {
        using var upstream = new AccountsStub();
        using var http = new HttpClient(upstream) { BaseAddress = new("https://characters.example/") };
        await Assert.ThrowsAsync<PlayerAdminApiException>(() => Client(http).GetAsync(AccountsStub.CharacterId, default, "lerya"));
    }

    [Fact]
    public async Task DetailRefusesAnotherCharacterEvenIfTheServiceReturnsAValidRecord()
    {
        var detail = AccountsStub.SavedCharacter();
        using var upstream = new AccountsStub { CharacterDetail = detail with { Character = detail.Character with { CharacterId = Guid.NewGuid().ToString("D") } } };
        using var http = new HttpClient(upstream) { BaseAddress = new("https://characters.example/") };
        var error = await Assert.ThrowsAsync<PlayerAdminApiException>(() => Client(http).GetAsync(AccountsStub.CharacterId, default));
        Assert.Equal(HttpStatusCode.BadGateway, error.StatusCode);
    }

    [Theory]
    [MemberData(nameof(InvalidDetails))]
    public async Task MalformedDetailCannotBecomePlausibleStatisticsOrHistory(AdminCharacterDetail detail)
    {
        using var upstream = new AccountsStub { CharacterDetail = detail };
        using var http = new HttpClient(upstream) { BaseAddress = new("https://characters.example/") };
        var error = await Assert.ThrowsAsync<PlayerAdminApiException>(() => Client(http).GetAsync(AccountsStub.CharacterId, default));
        Assert.Equal(HttpStatusCode.BadGateway, error.StatusCode);
        Assert.DoesNotContain(AccountsStub.Key, error.Message);
    }

    public static IEnumerable<object[]> InvalidDetails()
    {
        var valid = AccountsStub.SavedCharacter();
        yield return [valid with { Level = -1 }];
        yield return [valid with { Gameplay = null! }];
        yield return [valid with { Gameplay = valid.Gameplay with { Vitals = null } }];
        yield return [valid with { Gameplay = valid.Gameplay with { Status = "invented" } }];
        yield return [valid with { Gameplay = valid.Gameplay with { Vitals = valid.Gameplay.Vitals! with { Health = 101 } } }];
        yield return [valid with { Gameplay = valid.Gameplay with { LearnedRankOne = uint.MaxValue } }];
        yield return [valid with { Gameplay = valid.Gameplay with { LearnedRankTwo = 2 } }];
        yield return [valid with { Gameplay = new("not_saved", valid.Gameplay.Vitals, "mage", [], 0, 0) }];
        yield return [valid with { Quests = valid.Quests with { Items = null! } }];
        yield return [valid with { Quests = valid.Quests with { Items = [valid.Quests.Items[0] with { Completed = true }] } }];
        yield return [valid with { Quests = valid.Quests with { Items = [valid.Quests.Items[0] with { Kills = 11 }] } }];
        yield return [valid with { Quests = valid.Quests with { Total = -1 } }];
        yield return [valid with { Bags = new("empty") }];
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{\"gameplay\":")]
    public async Task MissingOrInvalidDetailPayloadIsRejected(string json)
    {
        using var upstream = new AccountsStub { CharacterDetailJson = json };
        using var http = new HttpClient(upstream) { BaseAddress = new("https://characters.example/") };
        var error = await Assert.ThrowsAsync<PlayerAdminApiException>(() => Client(http).GetAsync(AccountsStub.CharacterId, default));
        Assert.Equal(HttpStatusCode.BadGateway, error.StatusCode);
    }

    private static CharactersAdminClient Client(HttpClient http) => new(http,
        Options.Create(new CharactersAdminApiOptions { Enabled = true, AdminKey = AccountsStub.Key }));
}
