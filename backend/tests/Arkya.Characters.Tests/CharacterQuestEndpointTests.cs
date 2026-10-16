using System.Net;
using System.Net.Http.Json;
using Arkya.Characters.Api.Contracts;

namespace Arkya.Characters.Tests;

public sealed class CharacterQuestEndpointTests
{
    private static readonly Guid Version = Guid.Parse("95f1b50c-85ba-49a6-9b75-e12745472cf4");

    [Theory]
    [InlineData(null)]
    [InlineData("invalid")]
    public async Task PlayerSessionsAndInvalidServiceKeysCannotReadOrWriteProgress(string? key)
    {
        await using var factory = new CharactersFactory();
        using var player = factory.PlayerClient();
        if (key is not null)
            player.DefaultRequestHeaders.Add("X-Arkya-Service-Key", key);
        var route = $"/internal/characters/{Guid.NewGuid():D}/quests";

        Assert.Equal(HttpStatusCode.Forbidden, (await player.GetAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await player.PutAsJsonAsync(route + "/hunt-wolves", Request(0, 0))).StatusCode);
        Assert.Equal(0, factory.Identity.Calls);
    }

    [Fact]
    public async Task MultipleServiceKeysAreRejected()
    {
        await using var factory = new CharactersFactory();
        using var server = factory.PlayerClient(null);
        server.DefaultRequestHeaders.Add("X-Arkya-Service-Key", new[] { CharactersFactory.ServiceKey, CharactersFactory.ServiceKey });
        Assert.Equal(HttpStatusCode.Forbidden,
            (await server.GetAsync($"/internal/characters/{Guid.NewGuid():D}/quests")).StatusCode);
    }

    [Fact]
    public async Task DedicatedAcceptsSavesRetriesAndListsWithoutConsultingIdentity()
    {
        await using var factory = new CharactersFactory();
        var character = await CreateCharacterAsync(factory);
        using var server = ServerClient(factory);
        server.DefaultRequestHeaders.Authorization = new("Bearer", "offline");
        var identityCalls = factory.Identity.Calls;
        var route = $"/internal/characters/{character.Id:D}/quests";
        var initial = (await server.GetFromJsonAsync<CharacterQuestsResponse>(route))!;
        Assert.Equal(character.Id, initial.CharacterId);
        Assert.Empty(initial.Quests);

        var accepted = await server.PutAsJsonAsync(route + "/hunt-wolves", Request(0, 0));
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(new CharacterQuestResponse("hunt-wolves", Version, 3, 0, false, 1),
            await accepted.Content.ReadFromJsonAsync<CharacterQuestResponse>());
        var saved = await server.PutAsJsonAsync(route + "/hunt-wolves", Request(3, 1));
        var completed = (await saved.Content.ReadFromJsonAsync<CharacterQuestResponse>())!;
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.True(completed.Completed);
        Assert.Equal(2, completed.Revision);
        var retried = await server.PutAsJsonAsync(route + "/hunt-wolves", Request(3, 1));
        Assert.Equal(completed, await retried.Content.ReadFromJsonAsync<CharacterQuestResponse>());
        Assert.Equal(completed, Assert.Single((await server.GetFromJsonAsync<CharacterQuestsResponse>(route))!.Quests));
        Assert.Equal(identityCalls, factory.Identity.Calls);

        using var player = factory.PlayerClient();
        Assert.Equal(HttpStatusCode.NotFound, (await player.GetAsync($"/v1/characters/{character.Id:D}/quests")).StatusCode);
    }

    [Fact]
    public async Task UnknownCharacterReturns404AndCannotBeCreatedByQuestWrites()
    {
        await using var factory = new CharactersFactory();
        using var server = ServerClient(factory);
        var route = $"/internal/characters/{Guid.NewGuid():D}/quests";
        Assert.Equal(HttpStatusCode.NotFound, (await server.GetAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await server.PutAsJsonAsync(route + "/hunt-wolves", Request(0, 0))).StatusCode);
        using var player = factory.PlayerClient();
        Assert.Empty((await player.GetFromJsonAsync<CharacterResponse[]>("/v1/characters"))!);
    }

    [Fact]
    public async Task InvalidWritesAndConflictsDoNotChangePersistedProgress()
    {
        await using var factory = new CharactersFactory();
        var character = await CreateCharacterAsync(factory);
        using var server = ServerClient(factory);
        var route = $"/internal/characters/{character.Id:D}/quests/hunt-wolves";
        Assert.Equal(HttpStatusCode.OK, (await server.PutAsJsonAsync(route, Request(2, 0))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await server.PutAsJsonAsync(route, Request(4, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await server.PutAsJsonAsync(route, Request(1, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await server.PutAsJsonAsync(route,
            new SaveCharacterQuestRequest(Guid.NewGuid().ToString("D"), 3, 3, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await server.PutAsJsonAsync(route, Request(3, 0))).StatusCode);
        var withClientState = new { catalogVersion = Version, requiredKills = 3, kills = 3, expectedRevision = 1, completed = true };
        Assert.Equal(HttpStatusCode.BadRequest, (await server.PutAsJsonAsync(route, withClientState)).StatusCode);
        Assert.Equal(2, Assert.Single((await server.GetFromJsonAsync<CharacterQuestsResponse>(
            $"/internal/characters/{character.Id:D}/quests"))!.Quests).Kills);
    }

    private static SaveCharacterQuestRequest Request(int kills, long revision) => new(Version.ToString("D"), 3, kills, revision);

    private static HttpClient ServerClient(CharactersFactory factory)
    {
        var client = factory.PlayerClient(null);
        client.DefaultRequestHeaders.Add("X-Arkya-Service-Key", CharactersFactory.ServiceKey);
        return client;
    }

    private static async Task<CharacterResponse> CreateCharacterAsync(CharactersFactory factory)
    {
        using var player = factory.PlayerClient();
        using var response = await player.PostAsJsonAsync("/v1/characters", new { name = "Aeria" });
        return (await response.Content.ReadFromJsonAsync<CharacterResponse>())!;
    }
}
