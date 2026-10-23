using Arkya.Realms.Api.Application;
using Arkya.Realms.Api.Domain;
using Microsoft.Extensions.Options;

namespace Arkya.Realms.Tests;

public sealed class RealmRuntimeReleaseTests
{
    [Fact]
    public async Task MigrationIsPersistedAndAdmissionWithdrawnBeforeTheClusterChanges()
    {
        var (original, options) = Configuration();
        var repository = new MemoryRealmRepository();
        await repository.TryCreateAsync(original, default);
        var deployer = new ObservingDeployer(async realm =>
        {
            var persisted = await repository.FindAsync(original.RealmId, default);
            Assert.Equal(realm, persisted);
            Assert.Equal(RealmStatus.Pending, persisted!.Status);
            Assert.Equal(4, persisted.Profile.Servers.Length);
            Assert.All(persisted.Servers, value => Assert.Equal(0, value.ReadyReplicas));
            Assert.Equal(original.Profile, persisted.RuntimeRelease!.PreviousProfile);
            Assert.Equal(original.ImageRelease, persisted.RuntimeRelease.PreviousImageRelease);
            Assert.Null(persisted.ImageRelease);
            return realm.Profile.Servers.Select(value => new ServerObservation(value.Id, 1, value.PublicEndpoint)
                { ReadyImage = realm.Profile.Image }).ToArray();
        });
        await new RealmReconciler(repository, deployer, Options.Create(options), new TestClock()).ReconcileAsync(original, default);
        var ready = (await repository.FindAsync(original.RealmId, default))!;
        Assert.Equal(RealmStatus.Ready, ready.Status);
        Assert.True(ready.RuntimeRelease!.Completed);
        Assert.Equal(original.RealmId, ready.RealmId);
        Assert.Equal(original.DeploymentIdentity, ready.DeploymentIdentity);
        Assert.Equal(original.RuntimeConfiguration, ready.RuntimeConfiguration);
        Assert.Equal(original.Revision + 1, ready.Revision);
        Assert.Equal("RuntimeMigration", Assert.Single(ready.Operations).Kind);
        Assert.Equal(1, deployer.Calls);
    }

    [Fact]
    public async Task LosingTheStoreCompareAndSwapCannotMutateClusterResources()
    {
        var (original, options) = Configuration();
        var repository = new MemoryRealmRepository();
        var newer = original with { Version = original.Version + 1 };
        await repository.TryCreateAsync(newer, default);
        var deployer = new ObservingDeployer(_ => Task.FromResult<ServerObservation[]>([]));
        await new RealmReconciler(repository, deployer, Options.Create(options), new TestClock()).ReconcileAsync(original, default);
        Assert.Equal(0, deployer.Calls);
        Assert.Same(newer, await repository.FindAsync(original.RealmId, default));
    }

    [Fact]
    public async Task MigrationRequiresAnExplicitAutomaticActivation()
    {
        var (original, options) = Configuration();
        options.RuntimeReleases[0].Automatic = false;
        var repository = new MemoryRealmRepository();
        await repository.TryCreateAsync(original, default);
        Assert.Same(original, await RealmRuntimeReleases.PrepareAsync(original, repository, options, new TestClock(), default));
        Assert.Null((await repository.FindAsync(original.RealmId, default))!.RuntimeRelease);
    }

    [Theory]
    [InlineData("fingerprint")]
    [InlineData("target")]
    [InlineData("namespace")]
    [InlineData("image")]
    [InlineData("secret")]
    [InlineData("overlap")]
    [InlineData("legacy-runtime")]
    [InlineData("not-ready")]
    public async Task UnapprovedOrUnsafeTransitionsAreRejectedBeforePersistence(string change)
    {
        var (original, options) = Configuration();
        switch (change)
        {
            case "fingerprint": options.RuntimeReleases[0].PreviousProfileSha256 = new('f', 64); break;
            case "target": options.Targets[0].RealmId = "other-realm"; break;
            case "namespace": options.Targets[0].Namespace = "other-namespace"; break;
            case "image": options.Profiles[1].Image = original.Profile.Image; break;
            case "secret": options.Profiles[1].EnvironmentSecretName = "another-secret"; break;
            case "overlap":
                original = original with { Profile = original.Profile with
                    { Servers = [original.Profile.Servers[0] with { Id = "island-starter-island-01" }] } };
                options.RuntimeReleases[0].PreviousProfileSha256 = RealmRuntimeReleases.Fingerprint(original.Profile);
                break;
            case "legacy-runtime": options.Profiles[1].Servers[0].ProtocolVersion = 1; options.Profiles[1].Servers[0].BuildId = "arkya-web-beta-v1"; break;
            case "not-ready": original = original with { Status = RealmStatus.Deploying }; break;
        }
        var repository = new MemoryRealmRepository();
        await repository.TryCreateAsync(original, default);
        Assert.Null(await RealmRuntimeReleases.PrepareAsync(original, repository, options, new TestClock(), default));
        Assert.Same(original, await repository.FindAsync(original.RealmId, default));
    }

    [Fact]
    public async Task PendingMigrationResumesButCannotBeChangedOrWithdrawnByConfiguration()
    {
        var (original, options) = Configuration();
        var repository = new MemoryRealmRepository();
        await repository.TryCreateAsync(original, default);
        var pending = (await RealmRuntimeReleases.PrepareAsync(original, repository, options, new TestClock(), default))!;
        Assert.Same(pending, await RealmRuntimeReleases.PrepareAsync(pending, repository, options, new TestClock(), default));
        options.Profiles[1].Image = Fixtures.Image;
        Assert.Null(await RealmRuntimeReleases.PrepareAsync(pending, repository, options, new TestClock(), default));
        options.RuntimeReleases = [];
        Assert.Null(await RealmRuntimeReleases.PrepareAsync(pending, repository, options, new TestClock(), default));
    }

    [Fact]
    public async Task FailedMigrationRetriesTheSameDurableTransition()
    {
        var (original, options) = Configuration();
        var repository = new MemoryRealmRepository();
        await repository.TryCreateAsync(original, default);
        var pending = (await RealmRuntimeReleases.PrepareAsync(original, repository, options, new TestClock(), default))!;
        var failed = pending with { Status = RealmStatus.Failed, Version = pending.Version + 1 };
        await repository.TryReplaceAsync(failed, pending.Version, default);
        var deployer = new ObservingDeployer(realm => Task.FromResult(realm.Profile.Servers.Select(server =>
            new ServerObservation(server.Id, 1, server.PublicEndpoint) { ReadyImage = realm.Profile.Image }).ToArray()));
        await new RealmReconciler(repository, deployer, Options.Create(options), new TestClock()).ReconcileAsync(failed, default);
        var ready = (await repository.FindAsync(original.RealmId, default))!;
        Assert.Equal(RealmStatus.Ready, ready.Status);
        Assert.Equal(pending.Revision, ready.Revision);
        Assert.True(ready.RuntimeRelease!.Completed);
    }

    [Fact]
    public void RuntimeReleaseConfigurationRejectsDuplicatesAndMalformedFingerprints()
    {
        var (_, options) = Configuration();
        Assert.True(options.IsValid());
        options.RuntimeReleases = [options.RuntimeReleases[0], options.RuntimeReleases[0]];
        Assert.False(options.IsValid());
        options.RuntimeReleases = [options.RuntimeReleases[0]];
        options.RuntimeReleases[0].PreviousProfileSha256 = "not-a-digest";
        Assert.False(options.IsValid());
    }

    [Fact]
    public async Task RuntimeMigrationSupportsAnUnboundApprovedTarget()
    {
        var (original, options) = Configuration();
        options.Targets[0].RealmId = null;
        Assert.True(options.IsValid());
        var repository = new MemoryRealmRepository();
        await repository.TryCreateAsync(original, default);
        var pending = await RealmRuntimeReleases.PrepareAsync(original, repository, options, new TestClock(), default);
        Assert.NotNull(pending);
        Assert.NotNull(pending.RuntimeRelease);
        Assert.Equal(original.TargetId, pending.TargetId);
        Assert.Equal(original.Namespace, pending.Namespace);
        Assert.True(RealmRuntimeReleases.Authorizes(options, pending));
    }

    [Fact]
    public void RuntimeReleaseAcceptsMultipleUnboundTargetsAndRejectsMismatchedOrDuplicatePins()
    {
        var (_, options) = Configuration();
        options.Targets[0].RealmId = "other-realm";
        Assert.False(options.IsValid());
        options.Targets[0].RealmId = null;
        options.Targets = [options.Targets[0], new()
        {
            Id = "realm-eu-two", DisplayName = "EU two", Namespace = "arkya-realm-eu-two"
        }];
        Assert.True(options.IsValid());
        options.Targets[0].RealmId = options.RuntimeReleases[0].RealmId;
        options.Targets[1].RealmId = options.RuntimeReleases[0].RealmId;
        Assert.False(options.IsValid());
    }

    [Theory]
    [InlineData("realm")]
    [InlineData("target")]
    [InlineData("namespace")]
    [InlineData("pin")]
    public void UnboundRuntimeReleaseStillRequiresItsRealmAndExactApprovedDestination(string change)
    {
        var (realm, options) = Configuration();
        options.Targets[0].RealmId = null;
        var release = options.RuntimeReleases[0];
        Assert.NotNull(RealmRuntimeReleases.ApprovedProfile(options, realm, release));
        switch (change)
        {
            case "realm": realm = realm with { RealmId = "another-realm" }; break;
            case "target": realm = realm with { TargetId = "another-target" }; break;
            case "namespace": realm = realm with { Namespace = "another-namespace" }; break;
            case "pin": options.Targets[0].RealmId = "another-realm"; break;
        }
        Assert.Null(RealmRuntimeReleases.ApprovedProfile(options, realm, release));
    }

    internal static (RealmRecord Realm, DeploymentOptions Options) Configuration()
    {
        var realm = Fixtures.Realm(RealmStatus.Ready) with { ImageRelease = new(3, "registry.example/arkya/dedicated@sha256:" + new string('b', 64), Fixtures.Image) };
        var options = Fixtures.Options();
        options.Targets[0].RealmId = realm.RealmId;
        options.Profiles = [options.Profiles[0], new DeploymentProfile
        {
            Id = "partition-handoff", DisplayName = "Partition handoff", Image = "registry.example/arkya/dedicated@sha256:" + new string('a', 64),
            TopologyTemplateServerId = "island", TopologyPublicEndpointTemplate = "wss://game.example/{realmId}/{serverId}/",
            Servers = [new() { Id = "island", Port = 17943, Transport = "WebSocket", PublicEndpoint = "wss://game.example/island/",
                ProtocolVersion = 2, BuildId = "arkya-partition-handoff-v1" }]
        }];
        options.RuntimeReleases = [new() { RealmId = realm.RealmId, ProfileId = "partition-handoff", Version = 1,
            PreviousProfileSha256 = RealmRuntimeReleases.Fingerprint(realm.Profile), Automatic = true,
            Topology = new([new("starter-island", 4, 1536, 1536, 0, 0)]) }];
        return (realm, options);
    }

    private sealed class ObservingDeployer(Func<RealmRecord, Task<ServerObservation[]>> observe) : IRealmDeployer
    {
        public int Calls { get; private set; }
        public Task<ServerObservation[]> ReconcileAsync(RealmRecord realm, CancellationToken cancellationToken)
        { Calls++; return observe(realm); }
    }
}
