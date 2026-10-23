using System.Net;
using System.Net.Http.Json;
using System.Text;
using Arkya.Realms.Api.Application;
using Arkya.Realms.Api.Contracts;
using Arkya.Realms.Api.Domain;
using Arkya.Realms.Api.Infrastructure;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace Arkya.Realms.Tests;

public sealed class RealmConfigurationTests
{
    private const string Actor = "discord:123456789012345678";

    [Theory]
    [InlineData(RealmStatus.Draft)]
    [InlineData(RealmStatus.Pending)]
    [InlineData(RealmStatus.Ready)]
    [InlineData(RealmStatus.Failed)]
    public async Task EveryRealmHasIndependentDefaultConfiguration(string status)
    {
        var repository = new MemoryRealmRepository();
        var realm = Fixtures.Realm(status);
        await repository.TryCreateAsync(realm, default);
        var response = (await Service(repository).GetAsync(realm.RealmId, default))!;
        Assert.Equal(0, response.Revision);
        Assert.Equal(1m, response.XpMultiplier);
        Assert.Equal(1m, response.HarvestMultiplier);
        Assert.Null(response.UpdatedAt);
        Assert.Null(response.UpdatedBy);
        Assert.Empty(response.History);
        Assert.Null(await Service(repository).GetSnapshotAsync("unknown", default));
    }

    [Fact]
    public async Task UpdatingRatesAuditsBothValuesWithoutChangingDeploymentRevisionOrRolloutState()
    {
        var repository = new MemoryRealmRepository();
        var realm = Fixtures.Realm(RealmStatus.Ready) with
        {
            Restart = new(1, 0) { Completed = true }, Servers = [new("area-west", 1, "192.0.2.1")]
        };
        await repository.TryCreateAsync(realm, default);
        var response = (await Service(repository).UpdateAsync(realm.RealmId, new(0, 2.125m, 0m, "Weekend event"), Actor, default)).Configuration!;
        Assert.Equal(1, response.Revision);
        Assert.Equal(2.125m, response.XpMultiplier);
        Assert.Equal(0m, response.HarvestMultiplier);
        Assert.Equal(Actor, response.UpdatedBy);
        Assert.Equal(new TestClock().Now, response.UpdatedAt);
        var change = Assert.Single(response.History);
        Assert.Equal(0, change.ExpectedRevision);
        Assert.Equal(1m, change.PreviousXpMultiplier);
        Assert.Equal(1m, change.PreviousHarvestMultiplier);
        Assert.Equal("Weekend event", change.Reason);
        var current = (await repository.FindAsync(realm.RealmId, default))!;
        Assert.Equal(realm.Version + 1, current.Version);
        Assert.Equal(realm, current with { Version = realm.Version, RuntimeConfiguration = realm.RuntimeConfiguration });
    }

    [Theory]
    [InlineData("0", "100")]
    [InlineData("0.001", "99.999")]
    [InlineData("1.2300", "1.000")]
    public async Task ValidRatesIncludeZeroHundredAndThreeDecimalPlaces(string xp, string harvest)
    {
        var repository = new MemoryRealmRepository();
        await repository.TryCreateAsync(Fixtures.Realm(), default);
        var request = new UpdateRealmConfigurationRequest(0, decimal.Parse(xp, System.Globalization.CultureInfo.InvariantCulture),
            decimal.Parse(harvest, System.Globalization.CultureInfo.InvariantCulture), "Tune rates");
        Assert.NotNull((await Service(repository).UpdateAsync("eu-one", request, Actor, default)).Configuration);
    }

    [Theory]
    [InlineData("-0.001", "1")]
    [InlineData("1", "100.001")]
    [InlineData("0.0001", "1")]
    [InlineData("1", "1.1234")]
    public async Task InvalidRatesAreRejectedBeforeMutation(string xp, string harvest)
    {
        var repository = new MemoryRealmRepository();
        var original = Fixtures.Realm();
        await repository.TryCreateAsync(original, default);
        var request = new UpdateRealmConfigurationRequest(0, decimal.Parse(xp, System.Globalization.CultureInfo.InvariantCulture),
            decimal.Parse(harvest, System.Globalization.CultureInfo.InvariantCulture), "Tune rates");
        Assert.Equal("invalid_configuration", (await Service(repository).UpdateAsync("eu-one", request, Actor, default)).Error);
        Assert.Same(original, await repository.FindAsync("eu-one", default));
    }

    [Theory]
    [InlineData(null, "Tune rates", "invalid_operator")]
    [InlineData("", "Tune rates", "invalid_operator")]
    [InlineData(Actor, "", "invalid_operation_reason")]
    [InlineData(Actor, "reason\nwith newline", "invalid_operation_reason")]
    public async Task RuntimeConfigurationRequiresBoundedOperatorAndReason(string? actor, string reason, string error)
    {
        var repository = new MemoryRealmRepository();
        var original = Fixtures.Realm();
        await repository.TryCreateAsync(original, default);
        Assert.Equal(error, (await Service(repository).UpdateAsync("eu-one", new(0, 2, 3, reason), actor, default)).Error);
        Assert.Same(original, await repository.FindAsync("eu-one", default));
    }

    [Fact]
    public async Task RetryingAnAcceptedChangeIsIdempotentButChangingItsIdentityConflicts()
    {
        var repository = new MemoryRealmRepository();
        await repository.TryCreateAsync(Fixtures.Realm(), default);
        var service = Service(repository);
        var request = new UpdateRealmConfigurationRequest(0, 2m, 3m, "Weekend event");
        var accepted = (await service.UpdateAsync("eu-one", request, Actor, default)).Configuration!;
        var retry = (await service.UpdateAsync("eu-one", request, Actor, default)).Configuration!;
        Assert.Equal(accepted.Revision, retry.Revision);
        Assert.Single(retry.History);
        Assert.Equal("configuration_revision_conflict", (await service.UpdateAsync("eu-one", request with { XpMultiplier = 4m }, Actor, default)).Error);
        Assert.Equal("configuration_revision_conflict", (await service.UpdateAsync("eu-one", request, "discord:other", default)).Error);
        Assert.Equal("configuration_revision_conflict", (await service.UpdateAsync("eu-one", request with { Reason = "Other reason" }, Actor, default)).Error);
        Assert.Single((await repository.FindAsync("eu-one", default))!.RuntimeConfiguration.History);
    }

    [Fact]
    public async Task ConcurrentEditorsCannotOverwriteEachOthersRates()
    {
        var repository = new MemoryRealmRepository();
        await repository.TryCreateAsync(Fixtures.Realm(), default);
        var first = Service(repository);
        var second = Service(repository);
        var results = await Task.WhenAll(
            first.UpdateAsync("eu-one", new(0, 2m, 3m, "First event"), Actor, default),
            second.UpdateAsync("eu-one", new(0, 5m, 6m, "Second event"), "discord:other", default));
        Assert.Single(results, result => result.Configuration is not null);
        Assert.Single(results, result => result.Error == "configuration_revision_conflict");
        var configuration = (await repository.FindAsync("eu-one", default))!.RuntimeConfiguration;
        Assert.Equal(1, configuration.Revision);
        Assert.Single(configuration.History);
    }

    [Fact]
    public async Task ConfigurationWriteRetriesConcurrentDeploymentObservationWithoutRollingItBack()
    {
        var repository = new ObservationRaceRepository();
        var realm = Fixtures.Realm(RealmStatus.Deploying);
        await repository.Inner.TryCreateAsync(realm, default);
        var result = await Service(repository).UpdateAsync(realm.RealmId, new(0, 2m, 3m, "Tune rates"), Actor, default);
        Assert.NotNull(result.Configuration);
        var current = (await repository.FindAsync(realm.RealmId, default))!;
        Assert.Equal(RealmStatus.Ready, current.Status);
        Assert.Equal(realm.Revision, current.Revision);
        Assert.Equal(2m, current.RuntimeConfiguration.XpMultiplier);
        Assert.Equal(realm.Version + 2, current.Version);
        Assert.Single(current.RuntimeConfiguration.History);
    }

    [Fact]
    public async Task StaleReconcilerCannotOverwriteConfigurationAndNextPassPreservesIt()
    {
        var repository = new MemoryRealmRepository();
        var pending = Fixtures.Realm();
        await repository.TryCreateAsync(pending, default);
        await Service(repository).UpdateAsync(pending.RealmId, new(0, 2m, 3m, "Tune rates"), Actor, default);
        var reconciler = new RealmReconciler(repository, new StubDeployer(), Options.Create(Fixtures.Options()), new TestClock());
        await reconciler.ReconcileAsync(pending, default);
        var configured = (await repository.FindAsync(pending.RealmId, default))!;
        Assert.Equal(2m, configured.RuntimeConfiguration.XpMultiplier);
        await reconciler.ReconcileAsync(configured, default);
        var ready = (await repository.FindAsync(pending.RealmId, default))!;
        Assert.Equal(RealmStatus.Ready, ready.Status);
        Assert.Equal(configured.RuntimeConfiguration, ready.RuntimeConfiguration);
        Assert.Equal(pending.Revision, ready.Revision);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HistoricalMongoDocumentsRestoreDefaultRates(bool explicitNull)
    {
        var document = RealmDocument.From(Fixtures.Realm()).ToBsonDocument();
        if (explicitNull) document["RuntimeConfiguration"] = BsonNull.Value;
        else document.Remove("RuntimeConfiguration");
        var restored = BsonSerializer.Deserialize<RealmDocument>(document).ToRecord();
        Assert.Equal(0, restored.RuntimeConfiguration.Revision);
        Assert.Equal(1m, restored.RuntimeConfiguration.XpMultiplier);
        Assert.Equal(1m, restored.RuntimeConfiguration.HarvestMultiplier);
        Assert.Empty(restored.RuntimeConfiguration.History);
    }

    [Fact]
    public async Task MongoRoundTripAndNewServiceRestoreExactDecimalRatesAndAudit()
    {
        var repository = new MemoryRealmRepository();
        await repository.TryCreateAsync(Fixtures.Realm(), default);
        await Service(repository).UpdateAsync("eu-one", new(0, 2.125m, 0.001m, "Tune rates"), Actor, default);
        var stored = (await repository.FindAsync("eu-one", default))!;
        var restored = BsonSerializer.Deserialize<RealmDocument>(RealmDocument.From(stored).ToBson()).ToRecord();
        var freshRepository = new MemoryRealmRepository();
        await freshRepository.TryCreateAsync(restored, default);
        var freshService = Service(freshRepository);
        Assert.Equal(new RealmConfigurationSnapshot(1, 2.125m, 0.001m), await freshService.GetSnapshotAsync("eu-one", default));
        var response = (await freshService.GetAsync("eu-one", default))!;
        Assert.Equal(Actor, response.UpdatedBy);
        Assert.Equal(new TestClock().Now, response.UpdatedAt);
        var change = Assert.Single(response.History);
        Assert.Equal(2.125m, change.XpMultiplier);
        Assert.Equal(0.001m, change.HarvestMultiplier);
        Assert.Equal("Tune rates", change.Reason);
    }

    [Fact]
    public async Task ConfigurationEndpointsRequireServiceKeyActorAndStrictBoundedJson()
    {
        using var factory = new RealmFactory(enabled: false);
        await factory.Repository.TryCreateAsync(Fixtures.Realm(RealmStatus.Ready), default);
        using var anonymous = factory.Client(key: null);
        Assert.Equal(HttpStatusCode.Forbidden, (await anonymous.GetAsync("/admin/realms/eu-one/configuration")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await anonymous.PutAsJsonAsync("/admin/realms/eu-one/configuration", new UpdateRealmConfigurationRequest(0, 2, 3, "Tune rates"))).StatusCode);
        using var client = factory.Client();
        Assert.Equal(1m, (await client.GetFromJsonAsync<RealmConfigurationResponse>("/admin/realms/eu-one/configuration"))!.XpMultiplier);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/admin/realms/eu-one/configuration", new UpdateRealmConfigurationRequest(0, 2, 3, "Tune rates"))).StatusCode);
        client.DefaultRequestHeaders.Add("X-Arkya-Admin-Actor", Actor);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/admin/realms/eu-one/configuration", new { expectedRevision = 0, xpMultiplier = 2, reason = "Missing harvest" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/admin/realms/eu-one/configuration", new { expectedRevision = 0, xpMultiplier = 2, harvestMultiplier = 3, reason = "Tune", shell = "unexpected" })).StatusCode);
        using var notFinite = new StringContent("{\"expectedRevision\":0,\"xpMultiplier\":\"NaN\",\"harvestMultiplier\":1,\"reason\":\"Tune\"}", Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsync("/admin/realms/eu-one/configuration", notFinite)).StatusCode);
        var accepted = await client.PutAsJsonAsync("/admin/realms/eu-one/configuration", new UpdateRealmConfigurationRequest(0, 2, 3, "Tune rates"));
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(1, (await accepted.Content.ReadFromJsonAsync<RealmConfigurationResponse>())!.Revision);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync("/admin/realms/eu-one/configuration", new UpdateRealmConfigurationRequest(0, 4, 3, "Other edit"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/admin/realms/unknown/configuration")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync("/admin/realms/unknown/configuration", new UpdateRealmConfigurationRequest(0, 1, 1, "Tune"))).StatusCode);
    }

    [MongoFact]
    public async Task RuntimeConfigurationSurvivesANewMongoRepositoryInstance()
    {
        var database = $"arkya_realms_config_test_{Guid.NewGuid():N}";
        var settings = MongoClientSettings.FromConnectionString(Environment.GetEnvironmentVariable("ARKYA_TEST_MONGO"));
        settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
        var client = new MongoClient(settings);
        var options = Options.Create(new MongoOptions { DatabaseName = database });
        try
        {
            var first = new MongoRealmRepository(client, options);
            await first.InitializeAsync(default);
            var realm = Fixtures.Realm(RealmStatus.Ready);
            await first.TryCreateAsync(realm, default);
            var result = await Service(first).UpdateAsync(realm.RealmId, new(0, 2.125m, 0.001m, "Tune rates"), Actor, default);
            Assert.NotNull(result.Configuration);
            var second = new MongoRealmRepository(client, options);
            var restored = (await second.FindAsync(realm.RealmId, default))!;
            Assert.Equal(realm.Revision, restored.Revision);
            Assert.Equal(new RealmConfigurationSnapshot(1, 2.125m, 0.001m), await Service(second).GetSnapshotAsync(realm.RealmId, default));
            Assert.Equal(Actor, Assert.Single(restored.RuntimeConfiguration.History).UpdatedBy);
        }
        finally { await client.DropDatabaseAsync(database); }
    }

    private static RealmConfigurationService Service(IRealmRepository repository) => new(repository, new TestClock());

    private sealed class ObservationRaceRepository : IRealmRepository
    {
        public MemoryRealmRepository Inner { get; } = new();
        private bool raced;
        public Task InitializeAsync(CancellationToken ct) => Inner.InitializeAsync(ct);
        public Task<bool> IsAvailableAsync(CancellationToken ct) => Inner.IsAvailableAsync(ct);
        public Task<IReadOnlyList<RealmRecord>> ListAsync(CancellationToken ct) => Inner.ListAsync(ct);
        public Task<RealmRecord?> FindAsync(string realmId, CancellationToken ct) => Inner.FindAsync(realmId, ct);
        public Task<bool> TryCreateAsync(RealmRecord realm, CancellationToken ct) => Inner.TryCreateAsync(realm, ct);
        public async Task<bool> TryReplaceAsync(RealmRecord realm, long expectedVersion, CancellationToken ct)
        {
            if (!raced)
            {
                raced = true;
                var current = (await Inner.FindAsync(realm.RealmId, ct))!;
                await Inner.TryReplaceAsync(current with { Version = current.Version + 1, Status = RealmStatus.Ready }, current.Version, ct);
            }
            return await Inner.TryReplaceAsync(realm, expectedVersion, ct);
        }
    }
}
