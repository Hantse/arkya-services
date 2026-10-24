using System.Net;
using System.Net.Http.Json;
using Arkya.Realms.Api.Application;
using Arkya.Realms.Api.Domain;
using Arkya.Realms.Api.Infrastructure;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace Arkya.Realms.Tests;

public sealed class RealmServerVersionTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";
    private static readonly PollRealmCommands Poll = new(Guid.NewGuid().ToString("D"), 1, ["announce"],
        GameVersion: "0.1.45", SourceRevision: Commit, SourceRunId: "12345678901");

    private static async Task<(RealmCommandService Service, MemoryRealmRepository Realms, MemoryRealmCommandRepository Store, TestClock Clock)> Fixture()
    {
        var realms = new MemoryRealmRepository();
        await realms.TryCreateAsync(Fixtures.Realm(RealmStatus.Ready), default);
        var store = new MemoryRealmCommandRepository();
        var clock = new TestClock();
        return (new(realms, store, Options.Create(new RealmRuntimeOptions()), clock), realms, store, clock);
    }

    [Fact]
    public async Task ReportsPersistAcrossServiceInstancesAndStaleReportsKeepTheirIdentity()
    {
        var (service, realms, store, clock) = await Fixture();
        Assert.Null(Assert.Single((await service.GetServerVersionsAsync("eu-one", default))!).GameVersion);
        Assert.NotNull((await service.PollAsync("eu-one", "area-west", Poll, default)).Value);
        var restarted = new RealmCommandService(realms, store, Options.Create(new RealmRuntimeOptions()), clock);
        var version = Assert.Single((await restarted.GetServerVersionsAsync("eu-one", default))!);
        Assert.Equal("0.1.45", version.GameVersion);
        Assert.Equal(Commit, version.SourceRevision);
        Assert.Equal(Poll.SourceRunId, version.SourceRunId);
        Assert.Equal(clock.Now, version.LastSeenAt);
        Assert.True(version.Available);
        var capability = Assert.Single((await restarted.GetAsync("eu-one", default))!.Servers);
        Assert.Equal(version.GameVersion, capability.GameVersion);
        Assert.Equal(version.SourceRevision, capability.SourceRevision);
        Assert.Equal(version.SourceRunId, capability.SourceRunId);

        clock.Now += TimeSpan.FromSeconds(31);
        var stale = Assert.Single((await restarted.GetServerVersionsAsync("eu-one", default))!);
        Assert.False(stale.Available);
        Assert.Equal(version with { Available = false }, stale);
    }

    [Fact]
    public async Task LegacyPollClearsPreviousInstanceVersionInsteadOfKeepingAnIncorrectRelease()
    {
        var (service, realms, _, _) = await Fixture();
        await service.PollAsync("eu-one", "area-west", Poll, default);
        var realm = (await realms.FindAsync("eu-one", default))!;
        await realms.TryReplaceAsync(realm with { Status = RealmStatus.Pending, Version = 2 }, realm.Version, default);
        Assert.False(Assert.Single((await service.GetServerVersionsAsync("eu-one", default))!).Available);
        await service.PollAsync("eu-one", "area-west", new(Guid.NewGuid().ToString("D"), 1, ["announce"]), default);
        var legacy = Assert.Single((await service.GetServerVersionsAsync("eu-one", default))!);
        Assert.Null(legacy.GameVersion);
        Assert.Null(legacy.SourceRevision);
        Assert.Null(legacy.SourceRunId);
    }

    [Theory]
    [InlineData(" 0.1.45", null, null)]
    [InlineData("0.1.45\n", null, null)]
    [InlineData("0.1/<script>", null, null)]
    [InlineData("é.1.45", null, null)]
    [InlineData(null, "short-sha", null)]
    [InlineData(null, "gggggggggggggggggggggggggggggggggggggggg", null)]
    [InlineData(null, null, "1e10")]
    [InlineData(null, null, "1234567890123456789012345")]
    public async Task InvalidMetadataDoesNotRefreshOrReplaceTheLastReport(string? version, string? revision, string? runId)
    {
        var (service, _, store, clock) = await Fixture();
        await service.PollAsync("eu-one", "area-west", Poll, default);
        var previous = await store.FindAsync("eu-one", "area-west", default);
        clock.Now += TimeSpan.FromSeconds(10);
        var invalid = Poll with { GameVersion = version, SourceRevision = revision, SourceRunId = runId };
        Assert.Equal("invalid_poll", (await service.PollAsync("eu-one", "area-west", invalid, default)).Error);
        Assert.Equal(previous, await store.FindAsync("eu-one", "area-west", default));
        Assert.Equal("invalid_poll", (await service.PollAsync("eu-one", "area-west", Poll with { GameVersion = new string('1', 65) }, default)).Error);
    }

    [Fact]
    public async Task EmptyDevelopmentMetadataAndSha256RevisionsAreAccepted()
    {
        var (service, _, _, _) = await Fixture();
        Assert.NotNull((await service.PollAsync("eu-one", "area-west", Poll with
            { GameVersion = "0.1.0-dev+local_1", SourceRevision = new string('A', 64), SourceRunId = "" }, default)).Value);
        var report = Assert.Single((await service.GetServerVersionsAsync("eu-one", default))!);
        Assert.Equal(new string('a', 64), report.SourceRevision);
        Assert.Null(report.SourceRunId);
        await service.PollAsync("eu-one", "area-west", Poll with { GameVersion = "", SourceRevision = "", SourceRunId = "" }, default);
        Assert.Null(Assert.Single((await service.GetServerVersionsAsync("eu-one", default))!).GameVersion);
    }

    [Fact]
    public async Task HttpVersionProjectionRequiresAdminAndAcceptsLegacyAndVersionedHeartbeats()
    {
        await using var factory = new CommandFactory();
        await factory.Realms.TryCreateAsync(Fixtures.Realm(RealmStatus.Ready), default);
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        const string path = "/admin/realms/eu-one/server-versions";
        const string pollPath = "/internal/realms/eu-one/servers/area-west/commands/poll";
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
        client.DefaultRequestHeaders.Add("X-Arkya-Dedicated-Key", RealmRuntimeKeys.DeriveDedicatedKey(CommandFactory.Seed, "eu-one", "area-west"));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(pollPath,
            new { instanceId = Poll.InstanceId, protocolVersion = 1, supportedCommands = new[] { "announce" } })).StatusCode);
        client.DefaultRequestHeaders.Add("X-Arkya-Admin-Key", RealmFactory.AdminKey);
        var legacy = Assert.Single((await client.GetFromJsonAsync<RealmServerVersion[]>(path))!);
        Assert.Null(legacy.GameVersion);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(pollPath, Poll)).StatusCode);
        var report = Assert.Single((await client.GetFromJsonAsync<RealmServerVersion[]>(path))!);
        Assert.Equal(Poll.GameVersion, report.GameVersion);
        Assert.Equal(Commit, report.SourceRevision);
        var json = await client.GetStringAsync(path);
        Assert.DoesNotContain(CommandFactory.Seed, json);
        Assert.DoesNotContain(Poll.InstanceId, json);
        Assert.DoesNotContain("commands", json);
        Assert.DoesNotContain("instanceId", json);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/admin/realms/missing/server-versions")).StatusCode);
        var realm = (await factory.Realms.FindAsync("eu-one", default))!;
        await factory.Realms.TryReplaceAsync(realm with { Status = RealmStatus.Deleted, Version = 2 }, realm.Version, default);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public void OldAndNewBsonReportsKeepTheSameSchemaAndMissingMetadataIsUnknown()
    {
        var state = new RealmCommandServer("eu-one", "area-west", 1, Poll.InstanceId, new TestClock().Now, ["announce"], [])
            { GameVersion = Poll.GameVersion, SourceRevision = Commit, SourceRunId = Poll.SourceRunId };
        var document = new RealmCommandDocument { Id = "eu-one/area-west", State = state };
        var bson = document.ToBsonDocument();
        var restored = BsonSerializer.Deserialize<RealmCommandDocument>(bson);
        Assert.Equal(state.GameVersion, restored.State.GameVersion);
        Assert.Equal(state.SourceRevision, restored.State.SourceRevision);
        Assert.Equal(state.SourceRunId, restored.State.SourceRunId);
        foreach (var field in new[] { "GameVersion", "SourceRevision", "SourceRunId" }) bson["State"].AsBsonDocument.Remove(field);
        var legacy = BsonSerializer.Deserialize<RealmCommandDocument>(bson);
        Assert.Null(legacy.State.GameVersion);
        Assert.Null(legacy.State.SourceRevision);
        Assert.Null(legacy.State.SourceRunId);
        Assert.Equal(1, legacy.SchemaVersion);
    }

    [MongoFact]
    public async Task MongoProjectionReadsVersionsWithoutRequiringCommandHistory()
    {
        var database = "arkya_versions_test_" + Guid.NewGuid().ToString("N");
        var client = new MongoClient(Environment.GetEnvironmentVariable("ARKYA_TEST_MONGO"));
        try
        {
            var repository = new MongoRealmCommandRepository(client, Options.Create(new MongoOptions { DatabaseName = database }));
            var state = new RealmCommandServer("eu-one", "area-west", 1, Poll.InstanceId, new TestClock().Now, ["announce"], [])
                { GameVersion = Poll.GameVersion, SourceRevision = Commit, SourceRunId = Poll.SourceRunId };
            Assert.True(await repository.TryWriteAsync(state, null, default));
            var report = Assert.Single(await repository.FindVersionsAsync("eu-one", ["area-west", "missing"], default));
            Assert.Equal(new RealmServerVersion("area-west", state.LastSeenAt, false, Poll.GameVersion, Commit, Poll.SourceRunId), report);
            Assert.Empty(await repository.FindVersionsAsync("eu-two", ["area-west"], default));
        }
        finally { await client.DropDatabaseAsync(database); }
    }
}
