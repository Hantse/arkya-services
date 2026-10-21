using Arkya.Realms.Api.Application;
using Arkya.Realms.Api.Domain;
using Microsoft.Extensions.Options;

namespace Arkya.Realms.Tests;

public sealed class RealmImageReleaseReconcilerTests
{
    private const string NextImage = "registry.example/arkya/dedicated@sha256:abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";
    private const string OtherImage = "registry.example/arkya/dedicated@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Theory]
    [InlineData(RealmStatus.Ready)]
    [InlineData(RealmStatus.Failed)]
    public async Task ImagePromotionIsDurableAndWithdrawsReadinessBeforeTheDeployerRuns(string status)
    {
        var repository = new MemoryRealmRepository();
        var original = Fixtures.Realm(status) with
        {
            LastError = "previous_error",
            Servers = Ready(Fixtures.Image)
        };
        await repository.TryCreateAsync(original, default);
        var clock = new TestClock { Now = original.UpdatedAt.AddDays(2) };
        var deployer = new RecordingDeployer
        {
            Observe = async requested =>
            {
                var persisted = (await repository.FindAsync(original.RealmId, default))!;
                Assert.Equal(requested, persisted);
                Assert.Equal(RealmStatus.Pending, persisted.Status);
                Assert.Equal(NextImage, persisted.Profile.Image);
                Assert.Equal(new RealmImageTransition(1, Fixtures.Image, NextImage), persisted.ImageRelease);
                Assert.Equal(original.Version + 1, persisted.Version);
                Assert.Equal(original.Revision + 1, persisted.Revision);
                Assert.Null(persisted.RequestedFromRevision);
                Assert.Null(persisted.LastError);
                Assert.Equal(clock.Now, persisted.DeploymentStartedAt);
                var observation = Assert.Single(persisted.Servers);
                Assert.Equal(0, observation.ReadyReplicas);
                Assert.Null(observation.Address);
                Assert.Null(observation.ReadyImage);
                return Ready(NextImage);
            }
        };

        await new RealmReconciler(repository, deployer, Options.Create(ReleaseOptions()), clock).ReconcileAsync(original, default);

        var ready = (await repository.FindAsync(original.RealmId, default))!;
        Assert.Equal(RealmStatus.Ready, ready.Status);
        Assert.Equal(original.Version + 2, ready.Version);
        Assert.Equal(original.Revision + 1, ready.Revision);
        Assert.Equal(original.Profile with { Image = NextImage }, ready.Profile);
        Assert.Equal(original.DeploymentIdentity, ready.DeploymentIdentity);
        Assert.Equal(original.RealmId, ready.RealmId);
        Assert.Equal(original.TargetId, ready.TargetId);
        Assert.Equal(original.Namespace, ready.Namespace);
        Assert.Single(deployer.Calls);
    }

    [Fact]
    public async Task StaleSnapshotCannotDeployWhenTheReleaseCompareAndSwapLoses()
    {
        var repository = new MemoryRealmRepository();
        var stale = Fixtures.Realm(RealmStatus.Ready);
        var current = stale with { Version = stale.Version + 1, UpdatedAt = stale.UpdatedAt.AddSeconds(1) };
        await repository.TryCreateAsync(current, default);
        var deployer = new RecordingDeployer();

        await new RealmReconciler(repository, deployer, Options.Create(ReleaseOptions()), new TestClock()).ReconcileAsync(stale, default);

        Assert.Same(current, await repository.FindAsync(stale.RealmId, default));
        Assert.Empty(deployer.Calls);
    }

    [Fact]
    public async Task RestartResumesTheSamePersistedReleaseWithoutRequestingAnotherRevision()
    {
        var repository = new MemoryRealmRepository();
        var persisted = ReleasedRealm(RealmStatus.Deploying);
        await repository.TryCreateAsync(persisted, default);
        var deployer = new RecordingDeployer();
        var options = Options.Create(ReleaseOptions());

        await new RealmReconciler(repository, deployer, options, new TestClock()).ReconcileAsync(persisted, default);
        var ready = (await repository.FindAsync(persisted.RealmId, default))!;
        await new RealmReconciler(repository, deployer, options, new TestClock()).ReconcileAsync(ready, default);

        var current = (await repository.FindAsync(persisted.RealmId, default))!;
        Assert.Equal(RealmStatus.Ready, current.Status);
        Assert.Equal(persisted.Revision, current.Revision);
        Assert.Equal(persisted.ImageRelease, current.ImageRelease);
        Assert.Equal(persisted.DeploymentIdentity, current.DeploymentIdentity);
        Assert.Equal(ready.Version, current.Version);
        Assert.Equal(2, deployer.Calls.Count);
        Assert.All(deployer.Calls, value => Assert.Equal(persisted.ImageRelease, value.ImageRelease));
    }

    [Theory]
    [InlineData("removed")]
    [InlineData("lower-version")]
    [InlineData("same-version-other-image")]
    [InlineData("same-version-other-previous-image")]
    public async Task MissingStaleOrContradictoryConfigurationCannotChangeAPersistedRelease(string configuration)
    {
        var repository = new MemoryRealmRepository();
        var persisted = ReleasedRealm(RealmStatus.Ready) with
        {
            ImageRelease = new RealmImageTransition(2, Fixtures.Image, NextImage, 1),
            Servers = Ready(NextImage)
        };
        await repository.TryCreateAsync(persisted, default);
        var options = ReleaseOptions();
        options.ImageReleases[0].Version = 2;
        switch (configuration)
        {
            case "removed": options.ImageReleases = []; break;
            case "lower-version": options.ImageReleases[0].Version = 1; break;
            case "same-version-other-image": options.Profiles[0].Image = OtherImage; break;
            case "same-version-other-previous-image": options.ImageReleases[0].PreviousImage = OtherImage; break;
        }
        var deployer = new RecordingDeployer();

        await new RealmReconciler(repository, deployer, Options.Create(options), new TestClock()).ReconcileAsync(persisted, default);

        Assert.Same(persisted, await repository.FindAsync(persisted.RealmId, default));
        Assert.Empty(deployer.Calls);
    }

    [Theory]
    [InlineData("previous-image")]
    [InlineData("profile")]
    [InlineData("server-arguments")]
    [InlineData("server-port")]
    [InlineData("environment-secret")]
    [InlineData("target-id")]
    [InlineData("target-namespace")]
    public async Task ImageReleaseCannotReplaceADifferentImageOrAlterFrozenConfiguration(string mismatch)
    {
        var repository = new MemoryRealmRepository();
        var original = Fixtures.Realm(RealmStatus.Ready);
        await repository.TryCreateAsync(original, default);
        var options = ReleaseOptions();
        switch (mismatch)
        {
            case "previous-image": options.ImageReleases[0].PreviousImage = OtherImage; break;
            case "profile":
                options.Profiles[0].Id = "another-profile";
                options.ImageReleases[0].ProfileId = "another-profile";
                break;
            case "server-arguments": options.Profiles[0].Servers[0].Args = ["-different"]; break;
            case "server-port": options.Profiles[0].Servers[0].Port++; break;
            case "environment-secret": options.Profiles[0].EnvironmentSecretName = "another-secret"; break;
            case "target-id": options.Targets[0].Id = "another-target"; break;
            case "target-namespace": options.Targets[0].Namespace = "arkya-another-namespace"; break;
        }
        Assert.True(options.IsValid());
        var deployer = new RecordingDeployer();

        await new RealmReconciler(repository, deployer, Options.Create(options), new TestClock()).ReconcileAsync(original, default);

        Assert.Same(original, await repository.FindAsync(original.RealmId, default));
        Assert.Empty(deployer.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(Fixtures.Image)]
    public async Task OldReplicaReadinessCannotEnableTheNewImageRelease(string? observedImage)
    {
        var repository = new MemoryRealmRepository();
        var original = Fixtures.Realm(RealmStatus.Ready);
        await repository.TryCreateAsync(original, default);
        var options = Options.Create(ReleaseOptions());
        var deployer = new RecordingDeployer { Observe = _ => Task.FromResult(Ready(observedImage)) };

        await new RealmReconciler(repository, deployer, options, new TestClock()).ReconcileAsync(original, default);

        var waiting = (await repository.FindAsync(original.RealmId, default))!;
        Assert.Equal(RealmStatus.Deploying, waiting.Status);
        Assert.Equal(NextImage, waiting.Profile.Image);
        Assert.Null(waiting.LastError);
        deployer.Observe = _ => Task.FromResult(Ready(NextImage));
        await new RealmReconciler(repository, deployer, options, new TestClock()).ReconcileAsync(waiting, default);
        Assert.Equal(RealmStatus.Ready, (await repository.FindAsync(original.RealmId, default))!.Status);
    }

    [Fact]
    public async Task FailedPersistedReleaseRetriesTheSameIntentAndCanRecover()
    {
        var repository = new MemoryRealmRepository();
        var original = Fixtures.Realm(RealmStatus.Ready);
        await repository.TryCreateAsync(original, default);
        var options = Options.Create(ReleaseOptions());
        var deployer = new RecordingDeployer
        {
            Observe = _ => Task.FromException<ServerObservation[]>(new DeploymentFailure("deployment_progress_failed"))
        };
        await new RealmReconciler(repository, deployer, options, new TestClock()).ReconcileAsync(original, default);
        var failed = (await repository.FindAsync(original.RealmId, default))!;
        Assert.Equal(RealmStatus.Failed, failed.Status);
        Assert.Equal("deployment_progress_failed", failed.LastError);
        Assert.Equal(NextImage, failed.Profile.Image);
        Assert.NotNull(failed.ImageRelease);
        deployer.Observe = _ => Task.FromResult(Ready(NextImage));

        await new RealmReconciler(repository, deployer, options, new TestClock()).ReconcileAsync(failed, default);

        var recovered = (await repository.FindAsync(original.RealmId, default))!;
        Assert.Equal(RealmStatus.Ready, recovered.Status);
        Assert.Null(recovered.LastError);
        Assert.Equal(failed.Revision, recovered.Revision);
        Assert.Equal(failed.ImageRelease, recovered.ImageRelease);
        Assert.Equal(original.DeploymentIdentity, recovered.DeploymentIdentity);
        Assert.Equal(2, deployer.Calls.Count);
    }

    [Fact]
    public async Task ANewerReleaseChainsFromAReadyPersistedImageAndVersion()
    {
        var repository = new MemoryRealmRepository();
        var persisted = ReleasedRealm(RealmStatus.Ready);
        await repository.TryCreateAsync(persisted, default);
        var options = ReleaseOptions();
        options.ImageReleases[0].Version = 2;
        options.ImageReleases[0].PreviousImage = NextImage;
        options.Profiles[0].Image = OtherImage;
        var deployer = new RecordingDeployer { Observe = realm => Task.FromResult(Ready(realm.Profile.Image)) };

        await new RealmReconciler(repository, deployer, Options.Create(options), new TestClock()).ReconcileAsync(persisted, default);

        var current = (await repository.FindAsync(persisted.RealmId, default))!;
        Assert.Equal(new RealmImageTransition(2, NextImage, OtherImage, 1), current.ImageRelease);
        Assert.Equal(OtherImage, current.Profile.Image);
        Assert.Equal(persisted.Revision + 1, current.Revision);
        Assert.Equal(persisted.DeploymentIdentity, current.DeploymentIdentity);
        Assert.Equal(RealmStatus.Ready, current.Status);
        Assert.Single(deployer.Calls);
    }

    [Theory]
    [InlineData(RealmStatus.Pending)]
    [InlineData(RealmStatus.Deploying)]
    [InlineData(RealmStatus.Failed)]
    public async Task ANewerReleaseCannotSupersedeAnUnfinishedPersistedTransition(string status)
    {
        var repository = new MemoryRealmRepository();
        var persisted = ReleasedRealm(status);
        await repository.TryCreateAsync(persisted, default);
        var options = ReleaseOptions();
        options.ImageReleases[0].Version = 2;
        options.ImageReleases[0].PreviousImage = NextImage;
        options.Profiles[0].Image = OtherImage;
        var deployer = new RecordingDeployer();

        await new RealmReconciler(repository, deployer, Options.Create(options), new TestClock()).ReconcileAsync(persisted, default);

        Assert.Same(persisted, await repository.FindAsync(persisted.RealmId, default));
        Assert.Empty(deployer.Calls);
    }

    [Fact]
    public async Task RealmCreatedWithTheCurrentImageKeepsNormalReadinessMonitoring()
    {
        var repository = new MemoryRealmRepository();
        var options = Options.Create(ReleaseOptions());
        var clock = new TestClock();
        var service = new RealmService(repository, options, clock);
        var created = (await service.CreateAsync(new("eu-one", "Europe", "realm-eu-one", "island-v1"), default)).Realm!;
        Assert.Equal(NextImage, created.Profile.Image);
        Assert.Null(created.ImageRelease);
        var pending = (await service.RequestDeploymentAsync(created.RealmId, 0, default)).Realm!;
        var deployer = new RecordingDeployer();
        var reconciler = new RealmReconciler(repository, deployer, options, clock);
        await reconciler.ReconcileAsync(pending, default);
        var ready = (await repository.FindAsync(created.RealmId, default))!;
        Assert.Equal(RealmStatus.Ready, ready.Status);
        deployer.Observe = _ => Task.FromResult<ServerObservation[]>([new("area-west", 0, null)]);
        await reconciler.ReconcileAsync(ready, default);
        var unavailable = (await repository.FindAsync(created.RealmId, default))!;
        Assert.Equal(RealmStatus.Deploying, unavailable.Status);
        Assert.Equal(pending.Revision, unavailable.Revision);
        Assert.Null(unavailable.ImageRelease);
        Assert.Equal(2, deployer.Calls.Count);
    }

    [Fact]
    public async Task UpdatingAProfileWithoutAnExplicitReleaseKeepsTheExistingRealmImage()
    {
        var repository = new MemoryRealmRepository();
        var original = Fixtures.Realm(RealmStatus.Ready);
        await repository.TryCreateAsync(original, default);
        var options = ReleaseOptions();
        options.ImageReleases = [];
        var deployer = new RecordingDeployer { Observe = realm => Task.FromResult(Ready(realm.Profile.Image)) };

        await new RealmReconciler(repository, deployer, Options.Create(options), new TestClock()).ReconcileAsync(original, default);

        var current = (await repository.FindAsync(original.RealmId, default))!;
        Assert.Equal(Fixtures.Image, current.Profile.Image);
        Assert.Null(current.ImageRelease);
        Assert.Equal(original.Revision, current.Revision);
        Assert.Equal(Fixtures.Image, Assert.Single(deployer.Calls).Profile.Image);
    }

    private static DeploymentOptions ReleaseOptions()
    {
        var options = Fixtures.Options();
        options.Targets[0].RealmId = "eu-one";
        options.Profiles[0].Image = NextImage;
        options.ImageReleases = [new()
        {
            RealmId = "eu-one", ProfileId = "island-v1", Version = 1, PreviousImage = Fixtures.Image
        }];
        return options;
    }

    private static RealmRecord ReleasedRealm(string status) => Fixtures.Realm(status) with
    {
        Profile = Fixtures.Realm().Profile with { Image = NextImage },
        Revision = 2,
        Version = 2,
        RequestedFromRevision = null,
        ImageRelease = new(1, Fixtures.Image, NextImage)
    };

    private static ServerObservation[] Ready(string? image) =>
        [new("area-west", 1, "192.0.2.42") { ReadyImage = image }];

    private sealed class RecordingDeployer : IRealmDeployer
    {
        public List<RealmRecord> Calls { get; } = [];
        public Func<RealmRecord, Task<ServerObservation[]>> Observe { get; set; } = _ => Task.FromResult(Ready(NextImage));
        public Task<ServerObservation[]> ReconcileAsync(RealmRecord realm, CancellationToken cancellationToken)
        {
            Calls.Add(realm);
            return Observe(realm);
        }
    }
}
