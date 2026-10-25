using Arkya.Realms.Api.Application;
using Arkya.Realms.Api.Contracts;
using Arkya.Realms.Api.Domain;
using Arkya.Realms.Api.Infrastructure;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace Arkya.Realms.Tests;

public sealed class RealmNativeReleaseIntegrationTests
{
    private const string Actor = "admin:operator";
    private const string PreviousImage = "ghcr.io/hantse/arkya-mmorpg-dedicated@sha256:241b6d9eca943b6701c52aa99e76746e6fa3c5c7886e4a03e0c1067ea5ee9693";
    private static readonly NativePublishedRelease Release62 = new("0.1.62", "cf745eb2efa087c5454b93ce28b60c28bcc9d726", "40141089077",
        "ghcr.io/hantse/arkya-mmorpg-dedicated@sha256:ae1333150e0a31def941eb88eb600fad3c0e0d8529cd28bf1a959e627a988e53", 9, "arkya-craft-progression-v2");
    private static readonly NativePublishedRelease Release63 = Release62 with
    {
        GameVersion = "0.1.63", SourceRevision = new string('b', 40), SourceRunId = "40141089078",
        Image = NativePublishedRelease.ImageRepository + "@sha256:" + new string('b', 64)
    };
    private static readonly RealmTopology Topology = new([new("starter-island", 4, 1536, 1536, 0, 0, 2, 2)]);

    [Fact]
    public async Task PublishedRuntimeIsProposedWithoutChangingNovaOrStartingAMigration()
    {
        var (realm, options, catalog) = Configuration();
        var repository = new MemoryRealmRepository();
        await repository.TryCreateAsync(realm, default);
        var service = Service(repository, options, catalog);

        var offered = service.GetOperationsOptions(realm);

        Assert.True(offered.CanUpdate);
        Assert.Equal("RuntimeMigration", offered.UpdateKind);
        Assert.Equal(5, offered.CurrentProtocolVersion);
        Assert.Equal(9, offered.TargetProtocolVersion);
        Assert.Equal(Release62.BuildId, offered.TargetBuildId);
        Assert.Equal(Release62.GameVersion, offered.TargetGameVersion);
        Assert.Equal(Release62.SourceRevision, offered.TargetSourceRevision);
        Assert.Equal(Release62.SourceRunId, offered.TargetSourceRunId);
        Assert.Equal(Release62.Image, offered.UpdateImage);
        Assert.Same(realm, await repository.FindAsync("nova", default));
        Assert.Null(realm.NativeRelease);

        var deployer = new ObservingDeployer(current =>
        {
            Assert.Equal(PreviousImage, current.Profile.Image);
            Assert.Null(current.NativeRelease);
            Assert.Null(current.RuntimeRelease);
        });
        await Reconciler(repository, deployer, options, catalog).ReconcileAsync(realm, default);
        var observed = (await repository.FindAsync("nova", default))!;
        Assert.Equal(RealmStatus.Ready, observed.Status);
        Assert.Equal(realm.Profile, observed.Profile);
        Assert.Null(observed.NativeRelease);
        Assert.Null(observed.RuntimeRelease);
        Assert.Empty(observed.Operations);
    }

    [Fact]
    public async Task ManualMigrationRetainsItsPublishedBuildWhenAnotherBuildArrivesAndTheServiceRestarts()
    {
        var (realm, options, catalog) = Configuration();
        var repository = new MemoryRealmRepository();
        await repository.TryCreateAsync(realm, default);
        var pending = (await Service(repository, options, catalog).RequestUpdateAsync("nova",
            new(realm.Revision, "Install published 0.1.62", Release62.Image), Actor, default)).Realm!;

        Assert.Equal(RealmStatus.Pending, pending.Status);
        Assert.Equal(Release62, pending.NativeRelease!.Release);
        Assert.Equal("RuntimeMigration", pending.NativeRelease.Kind);
        Assert.Equal(62, pending.RuntimeRelease!.Version);
        Assert.Equal(realm.Profile, pending.RuntimeRelease.PreviousProfile);
        Assert.Equal(realm.DeploymentIdentity, pending.DeploymentIdentity);
        Assert.Equal(realm.TargetId, pending.TargetId);
        Assert.Equal(realm.Namespace, pending.Namespace);
        Assert.Equal(realm.RuntimeConfiguration, pending.RuntimeConfiguration);
        Assert.All(pending.Servers, server => Assert.Equal(0, server.ReadyReplicas));
        Assert.Equal("RuntimeMigration", Assert.Single(pending.Operations).Kind);

        Assert.True(catalog.Accept(Release63));
        var restartedCatalog = Catalog(Release63);
        var deployer = new ObservingDeployer(current =>
        {
            Assert.Equal(Release62.Image, current.Profile.Image);
            Assert.Equal(Release62, current.NativeRelease!.Release);
            Assert.True(RealmRuntimeReleases.Authorizes(restartedCatalog.Select(options, current, propose: false).Options, current));
        });
        await Reconciler(repository, deployer, options, restartedCatalog).ReconcileAsync(pending, default);

        var ready = (await repository.FindAsync("nova", default))!;
        Assert.Equal(RealmStatus.Ready, ready.Status);
        Assert.True(ready.RuntimeRelease!.Completed);
        Assert.Equal(Release62, ready.NativeRelease!.Release);
        Assert.Equal(Release62.Image, ready.Profile.Image);
        var offered = Service(repository, options, restartedCatalog).GetOperationsOptions(ready);
        Assert.True(offered.CanUpdate);
        Assert.Equal("Image", offered.UpdateKind);
        Assert.Equal(9, offered.CurrentProtocolVersion);
        Assert.Equal(9, offered.TargetProtocolVersion);
        Assert.Equal(Release63.GameVersion, offered.TargetGameVersion);
        Assert.Equal(Release63.SourceRevision, offered.TargetSourceRevision);
        Assert.Equal(Release63.SourceRunId, offered.TargetSourceRunId);
        Assert.Equal(Release63.Image, offered.UpdateImage);
        Assert.Equal(1, deployer.Calls);
    }

    [Fact]
    public async Task RestartKeepsTheInstalledBuildAndTheNextImageStillRequiresAnExplicitUpdate()
    {
        var (realm, options, catalog) = Configuration();
        var repository = new MemoryRealmRepository();
        await repository.TryCreateAsync(realm, default);
        var service = Service(repository, options, catalog);
        var pending = (await service.RequestUpdateAsync("nova", new(realm.Revision, "Install 0.1.62", Release62.Image), Actor, default)).Realm!;
        var deployer = new ObservingDeployer(_ => { });
        var reconciler = Reconciler(repository, deployer, options, catalog);
        await reconciler.ReconcileAsync(pending, default);
        var ready62 = (await repository.FindAsync("nova", default))!;
        Assert.True(catalog.Accept(Release63));
        Assert.Equal("0.1.63", service.GetOperationsOptions(ready62).TargetGameVersion);

        var restarting = (await service.RequestRestartAsync("nova", new(ready62.Revision, "Restart current build"), Actor, default)).Realm!;
        Assert.Equal(ready62.NativeRelease, restarting.NativeRelease);
        Assert.Equal(Release62.Image, restarting.Profile.Image);
        await reconciler.ReconcileAsync(restarting, default);
        var restarted = (await repository.FindAsync("nova", default))!;
        Assert.Equal(RealmStatus.Ready, restarted.Status);
        Assert.True(restarted.Restart!.Completed);
        Assert.Equal(Release62.Image, restarted.Profile.Image);
        Assert.Equal(Release62, restarted.NativeRelease!.Release);

        var updating = (await service.RequestUpdateAsync("nova", new(restarted.Revision, "Install 0.1.63", Release63.Image), Actor, default)).Realm!;
        Assert.Equal(RealmStatus.Pending, updating.Status);
        Assert.Equal("Image", updating.NativeRelease!.Kind);
        Assert.Equal(Release63, updating.NativeRelease.Release);
        Assert.Equal(Release62.Image, updating.ImageRelease!.PreviousImage);
        Assert.Equal(63, updating.ImageRelease.Version);
        Assert.Equal(restarted.Profile.Servers, updating.Profile.Servers);
        Assert.True(RealmImageReleases.Authorizes(catalog.Select(options, updating, propose: false).Options, updating));
        await reconciler.ReconcileAsync(updating, default);
        var ready63 = (await repository.FindAsync("nova", default))!;
        Assert.Equal(RealmStatus.Ready, ready63.Status);
        Assert.Equal(Release63.Image, ready63.Profile.Image);
        Assert.False(service.GetOperationsOptions(ready63).CanUpdate);
    }

    [Fact]
    public async Task FailedMigrationRetriesThePinnedBuildInsteadOfTheLatestPublishedBuild()
    {
        var (realm, options, catalog) = Configuration();
        var repository = new MemoryRealmRepository();
        await repository.TryCreateAsync(realm, default);
        var service = Service(repository, options, catalog);
        var pending = (await service.RequestUpdateAsync("nova", new(realm.Revision, "Install 0.1.62", Release62.Image), Actor, default)).Realm!;
        var failed = pending with { Status = RealmStatus.Failed, Version = pending.Version + 1, LastError = "cluster_unavailable" };
        Assert.True(await repository.TryReplaceAsync(failed, pending.Version, default));
        Assert.True(catalog.Accept(Release63));

        var offered = service.GetOperationsOptions(failed);
        Assert.True(offered.CanUpdate);
        Assert.Equal("0.1.62", offered.TargetGameVersion);
        var retried = (await service.RequestUpdateAsync("nova", new(failed.Revision, "Retry 0.1.62", Release62.Image), Actor, default)).Realm!;
        Assert.Same(failed.RuntimeRelease, retried.RuntimeRelease);
        Assert.Equal(failed.NativeRelease, retried.NativeRelease);
        Assert.Equal(Release62.Image, retried.Profile.Image);
        Assert.Equal(2, retried.Operations.Length);
    }

    [Fact]
    public async Task InvalidPersistedPinCannotAuthorizeAnOperationOrReachTheDeployer()
    {
        var (realm, options, catalog) = Configuration();
        var repository = new MemoryRealmRepository();
        await repository.TryCreateAsync(realm, default);
        var service = Service(repository, options, catalog);
        var pending = (await service.RequestUpdateAsync("nova", new(realm.Revision, "Install 0.1.62", Release62.Image), Actor, default)).Realm!;
        var invalid = pending with
        {
            NativeRelease = pending.NativeRelease! with { TargetProfileSha256 = new string('f', 64) },
            Status = RealmStatus.Failed, Version = pending.Version + 1
        };
        Assert.True(await repository.TryReplaceAsync(invalid, pending.Version, default));
        Assert.True(catalog.Accept(Release63));
        Assert.False(service.GetOperationsOptions(invalid).CanUpdate);
        Assert.False(service.GetOperationsOptions(invalid).CanRestart);
        Assert.Null((await service.RequestUpdateAsync("nova", new(invalid.Revision, "Reject invalid pin", Release62.Image), Actor, default)).Realm);
        var deployer = new ObservingDeployer(_ => throw new InvalidOperationException("An invalid pin reached the cluster."));
        await Reconciler(repository, deployer, options, catalog).ReconcileAsync(invalid, default);
        Assert.Equal(0, deployer.Calls);
        Assert.Same(invalid, await repository.FindAsync("nova", default));
    }

    [Fact]
    public async Task NativePinRoundTripsSchemaFourAndResumesIndependentlyOfTheCurrentPublication()
    {
        var (realm, options, catalog) = Configuration();
        var repository = new MemoryRealmRepository();
        await repository.TryCreateAsync(realm, default);
        var pending = (await Service(repository, options, catalog).RequestUpdateAsync("nova",
            new(realm.Revision, "Persist 0.1.62", Release62.Image), Actor, default)).Realm!;
        var bson = RealmDocument.From(pending).ToBsonDocument();

        Assert.Equal(4, bson["SchemaVersion"].AsInt32);
        Assert.True(bson.Contains("NativeRelease"));
        var restored = BsonSerializer.Deserialize<RealmDocument>(bson).ToRecord();
        Assert.Equal(pending.NativeRelease, restored.NativeRelease);
        Assert.Equal(pending.DeploymentIdentity, restored.DeploymentIdentity);
        Assert.Equal(pending.Revision, restored.Revision);
        Assert.Equal(pending.Version, restored.Version);
        Assert.Equal(RealmRuntimeReleases.Fingerprint(pending.Profile), RealmRuntimeReleases.Fingerprint(restored.Profile));
        Assert.True(RealmRuntimeReleases.Authorizes(Catalog(Release63).Select(options, restored, propose: false).Options, restored));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void LegacyDocumentsKeepTheirSchemaAndDoNotAcquireANativePin(int schema)
    {
        var (nova, _, _) = Configuration();
        var original = schema == 1 ? Fixtures.Realm() : nova;
        if (schema == 3) original = original with { RuntimeRelease = new(1, original.Profile, null, null) { Completed = true } };
        var bson = RealmDocument.From(original).ToBsonDocument();

        Assert.Equal(schema, bson["SchemaVersion"].AsInt32);
        Assert.False(bson.Contains("NativeRelease"));
        var restored = BsonSerializer.Deserialize<RealmDocument>(bson).ToRecord();
        Assert.Null(restored.NativeRelease);
        Assert.Equal(original.DeploymentIdentity, restored.DeploymentIdentity);
        Assert.Equal(RealmRuntimeReleases.Fingerprint(original.Profile), RealmRuntimeReleases.Fingerprint(restored.Profile));
        Assert.Equal(original.RuntimeRelease?.Version, restored.RuntimeRelease?.Version);
    }

    private static (RealmRecord Realm, DeploymentOptions Options, RealmNativeReleaseCatalog Catalog) Configuration()
    {
        var previous = Profile("nova-native-v5", PreviousImage, "v5", 5, "arkya-town-npcs-v1");
        var next = Profile("nova-native-v9", Release62.Image, "v9", 9, Release62.BuildId);
        var realm = Fixtures.Realm(RealmStatus.Ready) with
        {
            RealmId = "nova", Profile = RealmTopologyBuilder.Snapshot(previous, "nova", Topology), Revision = 2,
            Servers = []
        };
        realm = realm with { Servers = Observations(realm) };
        var options = Fixtures.Options();
        options.Profiles = [previous, next];
        options.Targets[0].RealmId = "nova";
        return (realm, options, Catalog(Release62));
    }

    private static DeploymentProfile Profile(string id, string image, string prefix, int protocolVersion, string buildId) => new()
    {
        Id = id, DisplayName = id, Image = image, ImagePullSecretName = "arkya-ghcr",
        TopologyTemplateServerId = "island", TopologyServerIdPrefix = prefix,
        TopologyPublicEndpointTemplate = "wss://api.arkya.online/game/{realmId}/{serverId}/",
        Servers = [new() { Id = "island", Port = 17943, Transport = "WebSocket",
            PublicEndpoint = "wss://api.arkya.online/game/nova/island/", ProtocolVersion = protocolVersion, BuildId = buildId }]
    };

    private static RealmNativeReleaseCatalog Catalog(NativePublishedRelease initial) => new(Options.Create(new RealmNativeReleaseOptions
    {
        Enabled = true, Targets = [new() { RealmId = "nova", ProfileId = "nova-native-v9" }], InitialRelease = initial
    }));

    private static RealmService Service(MemoryRealmRepository repository, DeploymentOptions options, IRealmNativeReleaseCatalog catalog) =>
        new(repository, Options.Create(options), new TestClock(), catalog);

    private static RealmReconciler Reconciler(MemoryRealmRepository repository, IRealmDeployer deployer, DeploymentOptions options,
        IRealmNativeReleaseCatalog catalog) => new(repository, deployer, Options.Create(options), new TestClock(), catalog);

    private static ServerObservation[] Observations(RealmRecord realm) => realm.Profile.Servers.Select(server =>
        new ServerObservation(server.Id, 1, server.PublicEndpoint)
            { ReadyImage = realm.Profile.Image, ReadyRestartRevision = realm.Restart?.Revision }).ToArray();

    private sealed class ObservingDeployer(Action<RealmRecord> observe) : IRealmDeployer
    {
        public int Calls { get; private set; }
        public Task<ServerObservation[]> ReconcileAsync(RealmRecord realm, CancellationToken cancellationToken)
        {
            Calls++;
            observe(realm);
            return Task.FromResult(Observations(realm));
        }
    }
}
