using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arkya.Characters.Api.Application;
using Arkya.Characters.Api.Domain;
using Arkya.Characters.Api.Infrastructure.Identity;
using Arkya.Characters.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arkya.Characters.Tests;

internal static class TestContext
{
    public static CancellationToken CancellationToken => CancellationToken.None;
}

internal sealed class FixedTimeProvider : TimeProvider
{
    public static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}

internal sealed class MemoryCharacterRepository : ICharacterRepository
{
    private readonly List<Character> characters = [];
    private readonly object gate = new();

    public Task<bool> ExistsAsync(Guid characterId, CancellationToken cancellationToken)
    {
        lock (gate)
            return Task.FromResult(characters.Any(character => character.Id == characterId));
    }

    public Task<IReadOnlyList<Character>> ListOwnedAsync(Guid accountId, CancellationToken cancellationToken)
    {
        lock (gate)
            return Task.FromResult<IReadOnlyList<Character>>(characters.Where(character => character.AccountId == accountId)
                .OrderBy(character => character.CreatedAt).ThenBy(character => character.Id).ToArray());
    }

    public Task<Character?> FindOwnedAsync(Guid characterId, Guid accountId, CancellationToken cancellationToken)
    {
        lock (gate)
            return Task.FromResult(characters.SingleOrDefault(character => character.Id == characterId && character.AccountId == accountId));
    }

    public Task<bool> TryAddAsync(Character character, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (characters.Any(existing => existing.NormalizedName == character.NormalizedName))
                return Task.FromResult(false);
            characters.Add(character);
            return Task.FromResult(true);
        }
    }
}

internal sealed class IdentityStub : HttpMessageHandler
{
    public static readonly Guid AliceId = Guid.Parse("ab1890c5-9930-4ac0-bafd-cd15a48bceca");
    public static readonly Guid BobId = Guid.Parse("24c3ed78-a71d-4b36-981d-668b52cbdb3b");
    public int Calls { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/internal/sessions/introspect", request.RequestUri!.AbsolutePath);
        Assert.Equal(CharactersFactory.IdentityKey, request.Headers.GetValues("X-Arkya-Service-Key").Single());
        Assert.Null(request.Headers.Authorization);
        var body = await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var token = body.GetProperty("sessionToken").GetString();
        if (token == "offline")
            throw new HttpRequestException("Identity unavailable.");
        if (token == "service-rejected")
            return new(HttpStatusCode.Forbidden);
        if (token == "invalid-json")
            return new(HttpStatusCode.OK) { Content = new StringContent("not-json") };
        if (token is not ("alice" or "bob" or "expired"))
            return new(HttpStatusCode.Unauthorized);

        return new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new PlayerSession(token == "bob" ? BobId : AliceId,
                token == "bob" ? "Bob" : "Alice", token == "expired" ? FixedTimeProvider.Now : FixedTimeProvider.Now.AddHours(1)))
        };
    }
}

internal sealed class CharactersFactory : WebApplicationFactory<Arkya.Characters.Api.Program>
{
    public const string ServiceKey = "characters-inbound-test-service-key-1234";
    public const string IdentityKey = "identity-outbound-test-service-key-1234";
    public IdentityStub Identity { get; } = new();
    public string EnvironmentName { get; init; } = "Development";
    public IReadOnlyDictionary<string, string?> AdditionalConfiguration { get; init; } = new Dictionary<string, string?>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ServiceAuthentication:Key"] = ServiceKey,
            ["Identity:BaseUrl"] = "https://identity.example/",
            ["Identity:ServiceKey"] = IdentityKey,
            ["Mongo:ConnectionString"] = "mongodb://localhost:27017",
            ["Mongo:DatabaseName"] = "arkya_characters_tests"
        }));
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(AdditionalConfiguration));
        builder.ConfigureServices(services =>
        {
            var initializer = services.Single(descriptor => descriptor.ImplementationType == typeof(CharacterIndexInitializer));
            services.Remove(initializer);
            services.RemoveAll<ICharacterRepository>();
            services.AddSingleton<ICharacterRepository, MemoryCharacterRepository>();
            services.RemoveAll<ICharacterQuestRepository>();
            services.AddSingleton<ICharacterQuestRepository, MemoryCharacterQuestRepository>();
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider, FixedTimeProvider>();
            services.AddHttpClient<ISessionValidator, IdentitySessionValidator>()
                .ConfigurePrimaryHttpMessageHandler(() => Identity);
        });
    }

    public HttpClient PlayerClient(string? token = "alice")
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        if (token is not null)
            client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }
}
