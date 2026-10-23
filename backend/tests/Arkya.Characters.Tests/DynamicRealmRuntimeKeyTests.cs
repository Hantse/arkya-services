using System.Net;
using System.Net.Http.Json;
using Arkya.Characters.Api.Application;
using Arkya.Characters.Api.Contracts;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Arkya.Characters.Tests;

public sealed class DynamicRealmRuntimeKeyTests
{
    private const string Seed = "dynamic-realm-runtime-seed-kept-only-in-secrets";
    private const string RealmId = "beta-another-realm";
    private const string ServerId = "island";
    private static GameRealmEntry Entry(bool ready = true) =>
        new(RealmId, "Another realm", ready, ServerId, "wss://game.example/", 1, GameProtocol.BuildId);

    private sealed class Catalog(params GameRealmEntry[] entries) : IGameRealmCatalog
    {
        public GameRealmEntry[] Entries { get; set; } = entries;
        public bool Unavailable { get; set; }
        public int Calls { get; private set; }
        public Task<GameRealmCatalog> ListAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            if (Unavailable) throw new HttpRequestException("Realm catalogue is unavailable.");
            return Task.FromResult(new GameRealmCatalog(Entries));
        }
    }

    private static DedicatedKeyValidator Validator(Catalog catalog) => new(Options.Create(new CharacterGameAccessOptions()),
        Options.Create(new CharacterRealmRuntimeOptions { DynamicRealmsEnabled = true, DedicatedKeySeed = Seed }), catalog);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CatalogueIdentityCanAuthenticateBeforeOrAfterItsRealmBecomesReady(bool ready)
    {
        var catalog = new Catalog(Entry(ready));
        var validator = Validator(catalog);
        var key = RealmRuntimeKeys.DeriveDedicatedKey(Seed, RealmId, ServerId);
        var identity = await validator.ResolveAsync(key, default);
        Assert.Equal(RealmId, identity!.RealmId);
        Assert.Equal(ServerId, identity.ServerId);
        Assert.Equal(key, identity.Key);
        Assert.True(validator.IsConfigured(RealmId, ServerId));
        Assert.False(validator.IsConfigured("../unsafe", ServerId));
        Assert.Equal(1, catalog.Calls);
    }

    [Fact]
    public async Task RemovingADeletedRealmFromTheCatalogueRevokesItsCredentialImmediately()
    {
        var catalog = new Catalog(Entry());
        var validator = Validator(catalog);
        var key = RealmRuntimeKeys.DeriveDedicatedKey(Seed, RealmId, ServerId);
        Assert.NotNull(await validator.ResolveAsync(key, default));
        catalog.Entries = [];
        Assert.Null(await validator.ResolveAsync(key, default));
        Assert.Equal(2, catalog.Calls);
    }

    [Fact]
    public async Task SeedsWrongKeysAndUncataloguedDestinationsCannotAuthenticate()
    {
        var validator = Validator(new Catalog(Entry()));
        foreach (var key in new[] { Seed, new string('A', 64),
                     RealmRuntimeKeys.DeriveDedicatedKey(Seed, "another-realm", ServerId),
                     RealmRuntimeKeys.DeriveDedicatedKey(Seed, RealmId, "undeclared-server") })
            Assert.Null(await validator.ResolveAsync(key, default));
        Assert.Null(validator.Resolve(RealmRuntimeKeys.DeriveDedicatedKey(Seed, RealmId, ServerId)));
    }

    [Fact]
    public async Task ValidPartitionCredentialsAreDerivedOnlyFromTheAuthenticatedCatalogue()
    {
        var entry = new GameRealmEntry(RealmId, "Partitioned realm", false, "island-first", "wss://game.example/first/",
            2, "arkya-partition-handoff-v1")
        {
            TopologyVersion = "partitioned-deployment",
            Partitions = [new("island-first", "wss://game.example/first/", "starter-island", 0, new(0, 0, 100, 100)),
                new("island-second", "wss://game.example/second/", "starter-island", 1, new(100, 0, 200, 100))]
        };
        var validator = Validator(new Catalog(entry));
        var identity = await validator.ResolveAsync(RealmRuntimeKeys.DeriveDedicatedKey(Seed, RealmId, "island-second"), default);
        Assert.Equal("island-second", identity!.ServerId);
        Assert.Null(await validator.ResolveAsync(RealmRuntimeKeys.DeriveDedicatedKey(Seed, RealmId, "island-third"), default));
    }

    [Fact]
    public async Task AnInvalidCatalogueCannotAuthorizeAnyDedicatedCredential()
    {
        var catalog = new Catalog(Entry(), Entry() with { RealmId = "../unsafe" });
        await Assert.ThrowsAsync<HttpRequestException>(() => Validator(catalog)
            .ResolveAsync(RealmRuntimeKeys.DeriveDedicatedKey(Seed, RealmId, ServerId), default));
        catalog.Entries = [Entry(), Entry()];
        await Assert.ThrowsAsync<HttpRequestException>(() => Validator(catalog)
            .ResolveAsync(RealmRuntimeKeys.DeriveDedicatedKey(Seed, RealmId, ServerId), default));
    }

    [Fact]
    public async Task AnUnsupportedLegacyEntryDoesNotBlockOtherRealmsAuthentication()
    {
        var unsupported = Entry() with { RealmId = "legacy-world", BuildId = "legacy-unsupported-build" };
        var validator = Validator(new Catalog(Entry(), unsupported));
        Assert.NotNull(await validator.ResolveAsync(RealmRuntimeKeys.DeriveDedicatedKey(Seed, RealmId, ServerId), default));
        Assert.Null(await validator.ResolveAsync(RealmRuntimeKeys.DeriveDedicatedKey(Seed, unsupported.RealmId, ServerId), default));
    }

    [Fact]
    public async Task CatalogueCancellationIsObserved()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Validator(new Catalog(Entry()))
            .ResolveAsync(RealmRuntimeKeys.DeriveDedicatedKey(Seed, RealmId, ServerId), cancellation.Token));
    }

    [Fact]
    public async Task AnArbitraryCatalogueRealmCanIssueAndConsumeACharacterAdmission()
    {
        var catalog = new Catalog(Entry());
        var validator = Validator(catalog);
        var clock = new GameClock();
        var accountId = Guid.NewGuid();
        var characters = new MemoryCharacterRepository();
        var character = (await new CharacterService(characters, clock).CreateAsync(accountId, "Aelys", default)).Character!;
        var service = new GameAdmissionService(characters, new MemoryGameLeaseStore(), catalog, validator, clock,
            new TestSessionReferences(clock, accountId), Options.Create(new CharacterGameAccessOptions()));
        var ticket = await service.IssueAsync(character.Id, accountId, clock.Now.AddHours(1),
            SessionReference.Create("dynamic-realm-session"), new AdmissionRequest(RealmId, 1, GameProtocol.BuildId), default);
        Assert.Null(ticket.Error);
        var server = await validator.ResolveAsync(RealmRuntimeKeys.DeriveDedicatedKey(Seed, RealmId, ServerId), default);
        var consumed = await service.ConsumeAsync(server!, new(ticket.Value!.Ticket), default);
        Assert.Null(consumed.Error);
        Assert.Equal(RealmId, consumed.Value!.RealmId);
    }

    private static CharactersFactory Factory(Catalog catalog, IReadOnlyDictionary<string, string?>? overrides = null)
    {
        var configuration = new Dictionary<string, string?>
        {
            ["RealmRuntime:DynamicRealmsEnabled"] = "true",
            ["RealmRuntime:DedicatedKeySeed"] = Seed,
            ["RealmRuntime:RealmId"] = "beta-web",
            ["RealmRuntime:ServerId"] = "island",
            ["Realms:BaseUrl"] = "https://realms.example/",
            ["Realms:ServiceKey"] = "private-realm-catalogue-key-not-a-dedicated-key"
        };
        foreach (var item in overrides ?? new Dictionary<string, string?>()) configuration[item.Key] = item.Value;
        return new CharactersFactory
        {
            AdditionalConfiguration = configuration,
            ConfigureTestServices = services =>
            {
                services.RemoveAll<IGameRealmCatalog>();
                services.AddSingleton<IGameRealmCatalog>(catalog);
                services.RemoveAll<IGameLeaseStore>();
                services.AddSingleton<IGameLeaseStore, MemoryGameLeaseStore>();
            }
        };
    }

    [Fact]
    public async Task HttpAuthenticationUsesTheLiveRealmInsteadOfLegacyStaticRealmConfiguration()
    {
        var catalog = new Catalog(Entry(false));
        await using var factory = Factory(catalog);
        using var client = factory.PlayerClient(null);
        client.DefaultRequestHeaders.Add("X-Arkya-Dedicated-Key", RealmRuntimeKeys.DeriveDedicatedKey(Seed, RealmId, ServerId));
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PostAsJsonAsync("/internal/game/admissions/consume", new { ticket = "invalid" })).StatusCode);
        catalog.Entries = [];
        var rejected = await client.PostAsJsonAsync("/internal/game/admissions/consume", new { ticket = "invalid" });
        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
        Assert.DoesNotContain(Seed, await rejected.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Remove("X-Arkya-Dedicated-Key");
        client.DefaultRequestHeaders.Add("X-Arkya-Dedicated-Key", Seed);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/internal/game/admissions/consume", new { ticket = "invalid" })).StatusCode);
    }

    [Fact]
    public async Task AnUnavailableCatalogueReturns503InsteadOfRejectingAValidKeyAsForbidden()
    {
        await using var factory = Factory(new Catalog(Entry()) { Unavailable = true });
        using var client = factory.PlayerClient(null);
        client.DefaultRequestHeaders.Add("X-Arkya-Dedicated-Key", RealmRuntimeKeys.DeriveDedicatedKey(Seed, RealmId, ServerId));
        var response = await client.PostAsJsonAsync("/internal/game/admissions/consume", new { ticket = "invalid" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("dependency_unavailable", await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain(Seed, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("RealmRuntime:DedicatedKeySeed", "")]
    [InlineData("RealmRuntime:DedicatedKeySeed", "short")]
    [InlineData("RealmRuntime:DedicatedKeySeed", " leading-whitespace-secret-is-not-supported")]
    [InlineData("GameAccess:DedicatedServers:0:Key", "explicit-key-that-must-not-be-overwritten")]
    [InlineData("Realms:BaseUrl", "")]
    public async Task InvalidOrAmbiguousDynamicConfigurationFailsAtStartup(string path, string value)
    {
        await using var factory = Factory(new Catalog(Entry()), new Dictionary<string, string?> { [path] = value });
        var error = Assert.ThrowsAny<Exception>(() => factory.PlayerClient(null));
        Assert.DoesNotContain(Seed, error.ToString());
    }

    [Fact]
    public async Task StaticExplicitCredentialsContinueToResolveWithoutContactingTheCatalogue()
    {
        var key = "static-explicit-dedicated-key-with-enough-characters";
        var catalog = new Catalog(Entry()) { Unavailable = true };
        var validator = new DedicatedKeyValidator(Options.Create(new CharacterGameAccessOptions
        {
            DedicatedServers = [new() { RealmId = RealmId, ServerId = ServerId, Key = key }]
        }), Options.Create(new CharacterRealmRuntimeOptions()), catalog);
        Assert.Equal(RealmId, (await validator.ResolveAsync(key, default))!.RealmId);
        Assert.Equal(0, catalog.Calls);
    }
}
