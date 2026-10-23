using Arkya.Realms.Api.Application;
using Arkya.Realms.Api.Contracts;
using Arkya.Realms.Api.Domain;
using Microsoft.Extensions.Options;

namespace Arkya.Realms.Tests;

public sealed class RealmReconcilerTests
{
    [Fact]
    public async Task SuccessfulUnchangedObservationsRefreshFreshnessWithoutChangingStateDate()
    {
        var repository = new MemoryRealmRepository();
        var original = Fixtures.Realm(RealmStatus.Ready) with { Servers = [new("area-west", 1, "192.0.2.1")] };
        await repository.TryCreateAsync(original, default);
        var clock = new TestClock { Now = original.UpdatedAt.AddSeconds(10) };
        var deployer = new StubDeployer();
        var reconciler = new RealmReconciler(repository, deployer, Options.Create(Fixtures.Options()), clock);

        await reconciler.ReconcileAsync(original, default);
        var first = (await repository.FindAsync(original.RealmId, default))!;
        Assert.Equal(clock.Now, first.LastObservedAt);
        Assert.Equal(original.UpdatedAt, first.UpdatedAt);
        Assert.Equal(original.Revision, first.Revision);
        clock.Now = clock.Now.AddSeconds(10);
        await reconciler.ReconcileAsync(first, default);
        var refreshed = (await repository.FindAsync(original.RealmId, default))!;
        Assert.Equal(clock.Now, refreshed.LastObservedAt);
        Assert.Equal(original.UpdatedAt, refreshed.UpdatedAt);
        Assert.Equal(first.Version + 1, refreshed.Version);

        deployer.Error = new HttpRequestException("private dependency response");
        clock.Now = clock.Now.AddSeconds(10);
        await reconciler.ReconcileAsync(refreshed, default);
        var unavailable = (await repository.FindAsync(original.RealmId, default))!;
        Assert.Equal(refreshed.LastObservedAt, unavailable.LastObservedAt);
        Assert.Equal(RealmStatus.Deploying, unavailable.Status);
    }

    [Fact]
    public async Task RestartResumesPersistedDeploymentAndReadinessNeedsReplicaAndExternalAddress()
    {
        var repository = new MemoryRealmRepository();
        var original = Fixtures.Realm(RealmStatus.Deploying);
        await repository.TryCreateAsync(original, default);
        var deployer = new StubDeployer { Observations = [new("area-west", 1, null)] };
        var reconciler = new RealmReconciler(repository, deployer, Options.Create(Fixtures.Options()), new TestClock());
        await reconciler.ReconcileAsync(original, default);
        var waiting = (await repository.FindAsync(original.RealmId, default))!;
        Assert.Equal(RealmStatus.Deploying, waiting.Status);
        deployer.Observations = [new("area-west", 1, "192.0.2.42")];
        await new RealmReconciler(repository, deployer, Options.Create(Fixtures.Options()), new TestClock()).ReconcileAsync(waiting, default);
        var ready = (await repository.FindAsync(original.RealmId, default))!;
        Assert.Equal(RealmStatus.Ready, ready.Status);
        Assert.Equal(original.DeploymentIdentity, ready.DeploymentIdentity);
        Assert.Equal(original.Revision, ready.Revision);
        Assert.Equal(2, deployer.Calls);
    }

    [Fact]
    public async Task StaleWorkerCannotOverwriteNewerObservation()
    {
        var repository = new MemoryRealmRepository();
        var original = Fixtures.Realm();
        await repository.TryCreateAsync(original, default);
        var first = new RealmReconciler(repository, new StubDeployer(), Options.Create(Fixtures.Options()), new TestClock());
        await first.ReconcileAsync(original, default);
        var stale = new RealmReconciler(repository, new StubDeployer { Error = new DeploymentFailure("cluster_access_denied") },
            Options.Create(Fixtures.Options()), new TestClock());
        await stale.ReconcileAsync(original, default);
        Assert.Equal(RealmStatus.Ready, (await repository.FindAsync(original.RealmId, default))!.Status);
    }

    [Fact]
    public async Task TimedOutDeploymentIsRetryableAndKeepsImmutableImageAndIdentity()
    {
        var repository = new MemoryRealmRepository();
        var original = Fixtures.Realm();
        await repository.TryCreateAsync(original, default);
        var clock = new TestClock { Now = original.UpdatedAt.AddMinutes(10) };
        var options = Fixtures.Options();
        var deployer = new StubDeployer { Observations = [new("area-west", 0, null)] };
        await new RealmReconciler(repository, deployer, Options.Create(options), clock).ReconcileAsync(original, default);
        var failed = (await repository.FindAsync(original.RealmId, default))!;
        Assert.Equal(RealmStatus.Failed, failed.Status);
        Assert.Equal("deployment_timeout", failed.LastError);
        options.Profiles[0].Image = "different-image";
        var retried = await new RealmService(repository, Options.Create(options), clock).RequestDeploymentAsync(original.RealmId, failed.Revision, default);
        Assert.Equal(RealmStatus.Pending, retried.Realm!.Status);
        Assert.Equal(2, retried.Realm.Revision);
        Assert.Equal(original.Profile.Image, retried.Realm.Profile.Image);
        Assert.Equal(original.DeploymentIdentity, retried.Realm.DeploymentIdentity);
    }

    [Fact]
    public async Task ReadyDeploymentIsMonitoredAndLostReadinessGetsANewDeadline()
    {
        var repository = new MemoryRealmRepository();
        var original = Fixtures.Realm(RealmStatus.Ready);
        await repository.TryCreateAsync(original, default);
        var clock = new TestClock { Now = original.UpdatedAt.AddDays(2) };
        var deployer = new StubDeployer { Observations = [new("area-west", 0, "192.0.2.42")] };
        await new RealmReconciler(repository, deployer, Options.Create(Fixtures.Options()), clock).ReconcileAsync(original, default);
        var current = (await repository.FindAsync(original.RealmId, default))!;
        Assert.Equal(RealmStatus.Deploying, current.Status);
        Assert.Equal(clock.Now, current.DeploymentStartedAt);
    }

    [Fact]
    public async Task TransientErrorsDoNotPersistDependencyMessagesAndEventuallyFail()
    {
        var repository = new MemoryRealmRepository();
        var original = Fixtures.Realm();
        await repository.TryCreateAsync(original, default);
        var clock = new TestClock();
        var deployer = new StubDeployer { Error = new HttpRequestException("secret response body must never escape") };
        var reconciler = new RealmReconciler(repository, deployer, Options.Create(Fixtures.Options()), clock);
        await reconciler.ReconcileAsync(original, default);
        var pending = (await repository.FindAsync(original.RealmId, default))!;
        Assert.Equal(RealmStatus.Deploying, pending.Status);
        Assert.Equal("cluster_unavailable", pending.LastError);
        clock.Now = clock.Now.AddMinutes(6);
        await reconciler.ReconcileAsync(pending, default);
        Assert.Equal(RealmStatus.Failed, (await repository.FindAsync(original.RealmId, default))!.Status);
    }

    [Fact]
    public async Task ConcurrentRequestsUseOneRevisionAndSnapshotTrustedArguments()
    {
        var repository = new MemoryRealmRepository();
        var options = Fixtures.Options();
        var service = new RealmService(repository, Options.Create(options), new TestClock());
        var created = (await service.CreateAsync(new CreateRealmRequest("eu-one", "Europe", "realm-eu-one", "island-v1"), default)).Realm!;
        options.Profiles[0].Servers[0].Args[0] = "changed";
        Assert.Equal("-batchmode", created.Profile.Servers[0].Args[0]);
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => service.RequestDeploymentAsync(created.RealmId, 0, default)));
        Assert.All(results, result => Assert.Equal(1, result.Realm!.Revision));
        Assert.Equal(1, (await repository.FindAsync(created.RealmId, default))!.Version);
    }
}
