using System.Net;
using System.Net.Http.Json;
using Arkya.Realms.Api.Application;
using Arkya.Realms.Api.Domain;
using Arkya.Realms.Api.Infrastructure;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace Arkya.Realms.Tests;

public sealed class RealmConfigurationPollingTests
{
    private static readonly string Instance = Guid.NewGuid().ToString("D");
    private static readonly PollRealmCommands Poll = new(Instance, 1, ["announce", "kick", "save"], 1, -1);

    private static async Task<(RealmCommandService Service, MemoryRealmRepository Realms)> Fixture(long revision = 0)
    {
        var realms = new MemoryRealmRepository();
        var realm = Fixtures.Realm(RealmStatus.Ready) with
        {
            RuntimeConfiguration = new() { Revision = revision, XpMultiplier = 2.5m, HarvestMultiplier = 1.125m }
        };
        await realms.TryCreateAsync(realm, default);
        return (new(realms, new MemoryRealmCommandRepository(), Options.Create(new RealmRuntimeOptions()), new TestClock()), realms);
    }

    [Fact]
    public async Task DesiredSnapshotIsDeliveredBeforeAcknowledgementAndOldClientsStayUnsupported()
    {
        var (service, _) = await Fixture();
        var response = (await service.PollAsync("eu-one", "area-west", Poll, default)).Value!;
        Assert.Equal(0, response.RuntimeConfiguration!.Revision);
        Assert.Equal(2.5m, response.RuntimeConfiguration.XpMultiplier);
        Assert.Equal(1.125m, response.RuntimeConfiguration.HarvestMultiplier);
        var capability = Assert.Single((await service.GetAsync("eu-one", default))!.Servers);
        Assert.Equal(1, capability.ConfigurationVersion);
        Assert.Null(capability.AppliedConfigurationRevision);
        var legacy = new PollRealmCommands(Instance, 1, ["announce"]);
        Assert.Null((await service.PollAsync("eu-one", "area-west", legacy, default)).Value!.RuntimeConfiguration);
        capability = Assert.Single((await service.GetAsync("eu-one", default))!.Servers);
        Assert.Equal(0, capability.ConfigurationVersion);
        Assert.Null(capability.AppliedConfigurationRevision);
    }

    [Fact]
    public async Task OnlyObservedAppliedRevisionIsReportedAndProcessRestartResetsIt()
    {
        var (service, _) = await Fixture(2);
        await service.PollAsync("eu-one", "area-west", Poll with { AppliedConfigurationRevision = 1 }, default);
        Assert.Equal(1, Assert.Single((await service.GetAsync("eu-one", default))!.Servers).AppliedConfigurationRevision);
        await service.PollAsync("eu-one", "area-west", Poll with { AppliedConfigurationRevision = 2 }, default);
        Assert.Equal(2, Assert.Single((await service.GetAsync("eu-one", default))!.Servers).AppliedConfigurationRevision);
        await service.PollAsync("eu-one", "area-west", Poll with { InstanceId = Guid.NewGuid().ToString("D") }, default);
        Assert.Null(Assert.Single((await service.GetAsync("eu-one", default))!.Servers).AppliedConfigurationRevision);
    }

    [Theory]
    [InlineData(1, 3)]
    [InlineData(1, -2)]
    [InlineData(2, 0)]
    [InlineData(0, 0)]
    public async Task UnsupportedCapabilityAndImpossibleAcknowledgementAreRejected(int version, long applied)
    {
        var (service, _) = await Fixture(2);
        Assert.Equal("invalid_poll", (await service.PollAsync("eu-one", "area-west",
            Poll with { ConfigurationVersion = version, AppliedConfigurationRevision = applied }, default)).Error);
        Assert.Null(Assert.Single((await service.GetAsync("eu-one", default))!.Servers).LastSeenAt);
    }

    [Fact]
    public async Task BusyServerReceivesLiveConfigurationWithoutClaimingAnotherCommand()
    {
        var (service, realms) = await Fixture(1);
        await service.PollAsync("eu-one", "area-west", Poll, default);
        var request = new EnqueueRealmCommand(Guid.NewGuid().ToString("D"), 1, "area-west", "save", "", "", "Test queued save");
        Assert.NotNull((await service.EnqueueAsync("eu-one", request, "admin:operator", default)).Value);
        var realm = (await realms.FindAsync("eu-one", default))!;
        Assert.True(await realms.TryReplaceAsync(realm with
        {
            Version = realm.Version + 1, RuntimeConfiguration = new() { Revision = 2, XpMultiplier = 3m, HarvestMultiplier = 4m }
        }, realm.Version, default));
        var busy = (await service.PollAsync("eu-one", "area-west", Poll with { AcceptCommands = false }, default)).Value!;
        Assert.Empty(busy.Commands);
        Assert.Equal(2, busy.RuntimeConfiguration!.Revision);
        Assert.Equal("Pending", Assert.Single((await service.GetAsync("eu-one", default))!.Commands).Status);
        Assert.Equal(request.RequestId, Assert.Single((await service.PollAsync("eu-one", "area-west", Poll, default)).Value!.Commands).Id);
    }

    [Fact]
    public async Task OneRealmSnapshotIsDeliveredIndependentlyToEveryDedicated()
    {
        var (service, realms) = await Fixture(2);
        var realm = (await realms.FindAsync("eu-one", default))!;
        Assert.True(await realms.TryReplaceAsync(realm with
        {
            Version = realm.Version + 1, Profile = realm.Profile with { Servers = [realm.Profile.Servers[0], new("area-east", 17844, [])] }
        }, realm.Version, default));
        var west = (await service.PollAsync("eu-one", "area-west", Poll with { AppliedConfigurationRevision = 2 }, default)).Value!;
        var east = (await service.PollAsync("eu-one", "area-east", Poll with { InstanceId = Guid.NewGuid().ToString("D") }, default)).Value!;
        Assert.Equal(west.RuntimeConfiguration, east.RuntimeConfiguration);
        var capabilities = (await service.GetAsync("eu-one", default))!.Servers;
        Assert.Equal(2, capabilities.Single(item => item.ServerId == "area-west").AppliedConfigurationRevision);
        Assert.Null(capabilities.Single(item => item.ServerId == "area-east").AppliedConfigurationRevision);
    }

    [Fact]
    public void ConfigurationAcknowledgementsPersistAndLegacyServerDocumentsDefaultToUnsupported()
    {
        var document = new RealmCommandDocument
        {
            Id = "eu-one/area-west", State = new("eu-one", "area-west", 1, Instance, new TestClock().Now, ["save"], [])
            { ConfigurationVersion = 1, AppliedConfigurationRevision = 7 }
        };
        var bson = document.ToBsonDocument();
        var restored = BsonSerializer.Deserialize<RealmCommandDocument>(bson);
        Assert.Equal(1, restored.State.ConfigurationVersion);
        Assert.Equal(7, restored.State.AppliedConfigurationRevision);
        bson["State"].AsBsonDocument.Remove("ConfigurationVersion");
        bson["State"].AsBsonDocument.Remove("AppliedConfigurationRevision");
        var legacy = BsonSerializer.Deserialize<RealmCommandDocument>(bson).State;
        Assert.Equal(0, legacy.ConfigurationVersion);
        Assert.Null(legacy.AppliedConfigurationRevision);
    }

    [Fact]
    public async Task HttpPollAcceptsAdditiveConfigurationFieldsWithDedicatedAuthorization()
    {
        await using var factory = new CommandFactory();
        await factory.Realms.TryCreateAsync(Fixtures.Realm(RealmStatus.Ready), default);
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        const string path = "/internal/realms/eu-one/servers/area-west/commands/poll";
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(path, Poll)).StatusCode);
        client.DefaultRequestHeaders.Add("X-Arkya-Dedicated-Key", RealmRuntimeKeys.DeriveDedicatedKey(CommandFactory.Seed, "eu-one", "area-west"));
        var response = await client.PostAsJsonAsync(path, Poll with { AcceptCommands = false });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var snapshot = (await response.Content.ReadFromJsonAsync<RealmCommandPollResponse>())!.RuntimeConfiguration!;
        Assert.Equal(0, snapshot.Revision);
        Assert.Equal(1m, snapshot.XpMultiplier);
        Assert.Equal(1m, snapshot.HarvestMultiplier);
    }
}
