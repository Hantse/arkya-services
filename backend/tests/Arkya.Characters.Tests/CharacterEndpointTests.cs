using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arkya.Characters.Api.Contracts;
using Arkya.Characters.Api.Domain;

namespace Arkya.Characters.Tests;

public sealed class CharacterEndpointTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("invalid")]
    [InlineData("expired")]
    [InlineData("arkya-dev-session")]
    public async Task PublicEndpointsRequireAnActiveIdentitySession(string? token)
    {
        await using var factory = new CharactersFactory();
        using var client = factory.PlayerClient(token);

        using var response = await client.GetAsync("/v1/characters");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateListAndGetAreOwnedByTheAuthenticatedAccount()
    {
        await using var factory = new CharactersFactory();
        using var alice = factory.PlayerClient();
        using var bob = factory.PlayerClient("bob");

        using var created = await alice.PostAsJsonAsync("/v1/characters", new { name = " Aeria " });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var character = (await created.Content.ReadFromJsonAsync<CharacterResponse>())!;
        Assert.NotEqual(Guid.Empty, character.Id);
        Assert.Equal("Aeria", character.Name);
        Assert.Equal(1, character.Level);
        Assert.Equal(CharacterAppearanceResponse.From(CharacterAppearance.Default), character.Appearance);
        Assert.Equal($"/v1/characters/{character.Id:D}", created.Headers.Location!.OriginalString);
        Assert.Equal(character, await alice.GetFromJsonAsync<CharacterResponse>(created.Headers.Location));
        Assert.Equal(character, Assert.Single((await alice.GetFromJsonAsync<CharacterResponse[]>("/v1/characters"))!));
        Assert.Empty((await bob.GetFromJsonAsync<CharacterResponse[]>("/v1/characters"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync(created.Headers.Location)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/v1/characters/{Guid.NewGuid():D}")).StatusCode);
    }

    [Fact]
    public async Task CustomAppearanceSurvivesCreateListGetAndAuthenticatedServerResolution()
    {
        await using var factory = new CharactersFactory();
        using var alice = factory.PlayerClient();
        using var bob = factory.PlayerClient("bob");
        var appearance = new CharacterAppearanceRequest(1, "elf", "b", 202, "copper", "long", "white", "amber", "ocean");
        using var created = await alice.PostAsJsonAsync("/v1/characters", new CreateCharacterRequest("Eloria", appearance));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var character = (await created.Content.ReadFromJsonAsync<CharacterResponse>())!;

        Assert.Equal(CharacterAppearanceResponse.From(appearance.ToDomain()), character.Appearance);
        Assert.Equal(1, character.Level);
        Assert.Equal(character, await alice.GetFromJsonAsync<CharacterResponse>(created.Headers.Location));
        Assert.Equal(character, Assert.Single((await alice.GetFromJsonAsync<CharacterResponse[]>("/v1/characters"))!));
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync(created.Headers.Location)).StatusCode);
        using var server = factory.PlayerClient(null);
        server.DefaultRequestHeaders.Add("X-Arkya-Service-Key", CharactersFactory.ServiceKey);
        var route = $"/internal/characters/{character.Id:D}/resolve";
        using var resolved = await server.PostAsJsonAsync(route, new { sessionToken = "alice" });
        Assert.Equal(character, (await resolved.Content.ReadFromJsonAsync<ResolvedCharacterResponse>())!.Character);
        Assert.Equal(HttpStatusCode.NotFound, (await server.PostAsJsonAsync(route, new { sessionToken = "bob" })).StatusCode);
    }

    [Theory]
    [InlineData("{\"name\":\"Aeria\",\"appearance\":{}}")]
    [InlineData("{\"name\":\"Aeria\",\"appearance\":{\"race\":\"elf\"}}")]
    [InlineData("{\"name\":\"Aeria\",\"appearance\":{\"version\":1,\"race\":\"human\",\"silhouette\":\"a\",\"heightCm\":175.5,\"skin\":\"warm\",\"hairStyle\":\"short\",\"hairColor\":\"brown\",\"eyeColor\":\"green\",\"outfitColor\":\"sage\"}}")]
    [InlineData("{\"name\":\"Aeria\",\"appearance\":{\"version\":1,\"race\":\"human\",\"silhouette\":\"a\",\"heightCm\":175,\"skin\":\"warm\",\"hairStyle\":\"short\",\"hairColor\":\"brown\",\"eyeColor\":\"green\",\"outfitColor\":\"sage\",\"speed\":99}}")]
    public async Task PartialAppearanceAndGameplayFieldsAreRejected(string json)
    {
        await using var factory = new CharactersFactory();
        using var client = factory.PlayerClient();
        using var response = await client.PostAsync("/v1/characters", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<CharacterResponse[]>("/v1/characters"))!);
    }

    [Theory]
    [MemberData(nameof(CharacterAppearanceTests.InvalidAppearances), MemberType = typeof(CharacterAppearanceTests))]
    public async Task InvalidAppearanceReturnsFieldErrors(CharacterAppearance appearance, string field)
    {
        await using var factory = new CharactersFactory();
        using var client = factory.PlayerClient();
        using var response = await client.PostAsJsonAsync("/v1/characters", new { name = "Aeria", appearance });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty($"appearance.{field}", out _));
        Assert.Empty((await client.GetFromJsonAsync<CharacterResponse[]>("/v1/characters"))!);
    }

    [Fact]
    public async Task AppearanceCatalogIsPublicReadOnlyAndDoesNotCallIdentity()
    {
        await using var factory = new CharactersFactory();
        using var client = factory.PlayerClient(null);
        var catalog = await client.GetFromJsonAsync<JsonElement>("/v1/characters/appearance-options");

        Assert.Equal(0, factory.Identity.Calls);
        Assert.Equal(1, catalog.GetProperty("version").GetInt32());
        Assert.Equal("human", catalog.GetProperty("defaults").GetProperty("race").GetString());
        Assert.Equal(2, catalog.GetProperty("races").GetArrayLength());
        Assert.Equal(8, catalog.GetProperty("palettes").GetProperty("skin").GetArrayLength());
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsJsonAsync("/v1/characters/appearance-options", new { })).StatusCode);
    }

    [Fact]
    public async Task ConcurrentCaseInsensitiveDuplicateCreationHasOneWinner()
    {
        await using var factory = new CharactersFactory();
        using var alice = factory.PlayerClient();
        using var bob = factory.PlayerClient("bob");

        var responses = await Task.WhenAll(alice.PostAsJsonAsync("/v1/characters", new { name = "Aeria" }),
            bob.PostAsJsonAsync("/v1/characters", new { name = " aeria " }));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        foreach (var response in responses)
            response.Dispose();
    }

    [Theory]
    [InlineData("{\"name\":\"Aeria\",\"level\":99}")]
    [InlineData("{\"name\":\"Aeria\",\"accountId\":\"ab1890c5-9930-4ac0-bafd-cd15a48bceca\"}")]
    [InlineData("{\"name\":\"Aeria\",\"inventory\":[]}")]
    [InlineData("{\"name\":\"A3\"}")]
    [InlineData("{\"name\":null}")]
    [InlineData("{}")]
    public async Task RejectsInvalidNamesAndCallerControlledState(string json)
    {
        await using var factory = new CharactersFactory();
        using var client = factory.PlayerClient();

        using var response = await client.PostAsync("/v1/characters", new StringContent(json,
            System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<CharacterResponse[]>("/v1/characters"))!);
    }

    [Theory]
    [InlineData("offline")]
    [InlineData("service-rejected")]
    [InlineData("invalid-json")]
    public async Task IdentityFailuresReturnServiceUnavailable(string token)
    {
        await using var factory = new CharactersFactory();
        using var client = factory.PlayerClient(token);

        using var response = await client.GetAsync("/v1/characters");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(CharactersFactory.IdentityKey, body);
        Assert.DoesNotContain(token, body);
    }

    [Fact]
    public async Task DedicatedServerResolvesOnlyOwnedCharactersAfterBothAuthentications()
    {
        await using var factory = new CharactersFactory();
        using var alice = factory.PlayerClient();
        var created = await alice.PostAsJsonAsync("/v1/characters", new { name = "Aeria" });
        var character = (await created.Content.ReadFromJsonAsync<CharacterResponse>())!;
        using var server = factory.PlayerClient(null);
        var route = $"/internal/characters/{character.Id:D}/resolve";
        var callsBefore = factory.Identity.Calls;

        server.DefaultRequestHeaders.Authorization = new("Bearer", "offline");
        var forbidden = await server.PostAsJsonAsync(route, new { sessionToken = "alice" });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(callsBefore, factory.Identity.Calls);
        server.DefaultRequestHeaders.Add("X-Arkya-Service-Key", CharactersFactory.ServiceKey);

        using var resolved = await server.PostAsJsonAsync(route, new { sessionToken = "alice" });
        Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);
        var resolution = (await resolved.Content.ReadFromJsonAsync<ResolvedCharacterResponse>())!;
        Assert.Equal(IdentityStub.AliceId, resolution.AccountId);
        Assert.Equal(character, resolution.Character);
        Assert.Equal(FixedTimeProvider.Now.AddHours(1), resolution.SessionExpiresAt);
        Assert.Equal(HttpStatusCode.NotFound, (await server.PostAsJsonAsync(route, new { sessionToken = "bob" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await server.PostAsJsonAsync(route, new { sessionToken = "invalid" })).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await server.PostAsJsonAsync(route, new { sessionToken = "offline" })).StatusCode);
    }
}
