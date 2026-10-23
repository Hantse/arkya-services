using System.Net;
using System.Net.Http.Json;
using Arkya.Realms.Api.Application;
using Arkya.Realms.Api.Contracts;
using Arkya.Realms.Api.Domain;
using Arkya.Realms.Api.Infrastructure;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace Arkya.Realms.Tests;

public sealed class RealmLifecycleTests
{
    private const string NextImage = "registry.example/arkya/dedicated@sha256:abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";
    private const string Actor = "discord:123456789012345678";

    [Fact]
    public async Task RestartPersistsOneIntentWithActorReasonAndRevisionBeforeExecution()
    {
        var repository = new MemoryRealmRepository();
        var original = Fixtures.Realm(RealmStatus.Ready);
        await repository.TryCreateAsync(original, default);
        var service = Service(repository);
        var request = new RestartRealmRequest(original.Revision, "Apply maintenance restart");
        var accepted = (await service.RequestRestartAsync(original.RealmId, request, Actor, default)).Realm!;
        Assert.Equal(RealmStatus.Pending, accepted.Status);
        Assert.Equal(original.Revision + 1, accepted.Revision);
        Assert.Equal(new RealmRestartTransition(accepted.Revision, 0), accepted.Restart);
        Assert.Null(accepted.RequestedFromRevision);
        Assert.Equal(original.Profile, accepted.Profile);
        Assert.Equal(original.DeploymentIdentity, accepted.DeploymentIdentity);
        Assert.All(accepted.Servers, server => Assert.Equal(0, server.ReadyReplicas));
        var operation = Assert.Single(accepted.Operations);
        Assert.Equal(Actor, operation.RequestedBy);
        Assert.Equal(request.Reason, operation.Reason);
        Assert.Equal("Restart", operation.Kind);
        Assert.Null(operation.CompletedAt);
        Assert.Same(accepted, (await service.RequestRestartAsync(original.RealmId, request, Actor, default)).Realm);
        Assert.Equal("revision_conflict", (await service.RequestRestartAsync(original.RealmId, request, "discord:other", default)).Error);
        Assert.Equal("revision_conflict", (await service.RequestDeploymentAsync(original.RealmId, original.Revision, default)).Error);
        Assert.Equal("operation_in_progress", (await service.RequestRestartAsync(original.RealmId, request with { ExpectedRevision = accepted.Revision }, Actor, default)).Error);
        Assert.Single((await repository.FindAsync(original.RealmId, default))!.Operations);
    }

    [Fact]
    public async Task ConcurrentRestartsAdmitOnlyOneDurableRevision()
    {
        var repository = new MemoryRealmRepository();
        var original = Fixtures.Realm(RealmStatus.Ready);
        await repository.TryCreateAsync(original, default);
        var service = Service(repository);
        var results = await Task.WhenAll(
            service.RequestRestartAsync(original.RealmId, new(original.Revision, "Operator one"), Actor, default),
            service.RequestRestartAsync(original.RealmId, new(original.Revision, "Operator two"), "discord:second", default));
        Assert.Single(results, result => result.Realm is not null);
        Assert.Single(results, result => result.Error == "revision_conflict");
        Assert.Single((await repository.FindAsync(original.RealmId, default))!.Operations);
    }

    [Fact]
    public async Task RestartFailureBeforeClusterPatchRetriesTheSameFenceAndCanLaterRestartAgain()
    {
        var repository = new MemoryRealmRepository();
        var original = Fixtures.Realm(RealmStatus.Ready);
        await repository.TryCreateAsync(original, default);
        var service = Service(repository);
        var requested = (await service.RequestRestartAsync(original.RealmId, new(original.Revision, "Restart"), Actor, default)).Realm!;
        var deployer = new StubDeployer { Error = new DeploymentFailure("cluster_access_denied") };
        var reconciler = new RealmReconciler(repository, deployer, Options.Create(Fixtures.Options()), new TestClock());
        await reconciler.ReconcileAsync(requested, default);
        var failed = (await repository.FindAsync(original.RealmId, default))!;
        Assert.Equal(RealmStatus.Failed, failed.Status);
        Assert.False(failed.Restart!.Completed);
        var retry = (await service.RequestRestartAsync(original.RealmId, new(failed.Revision, "Retry restart"), Actor, default)).Realm!;
        Assert.Equal(failed.Restart, retry.Restart);
        Assert.Equal(failed.Revision + 1, retry.Revision);
        deployer.Error = null;
        deployer.Observations = [new("area-west", 1, "192.0.2.1") { ReadyRestartRevision = retry.Restart!.Revision }];
        await reconciler.ReconcileAsync(retry, default);
        var ready = (await repository.FindAsync(original.RealmId, default))!;
        Assert.True(ready.Restart!.Completed);
        deployer.Error = new DeploymentFailure("cluster_access_denied");
        await reconciler.ReconcileAsync(ready, default);
        var unhealthy = (await repository.FindAsync(original.RealmId, default))!;
        var fresh = (await service.RequestRestartAsync(original.RealmId, new(unhealthy.Revision, "Restart unhealthy process"), Actor, default)).Realm!;
        Assert.Equal(new RealmRestartTransition(fresh.Revision, ready.Restart.Revision), fresh.Restart);
        Assert.Equal(RealmStatus.Failed, fresh.Operations[0].Status);
        Assert.Equal(RealmStatus.Ready, fresh.Operations[1].Status);
    }

    [Theory]
    [InlineData(null, "A reason", "invalid_operator")]
    [InlineData("", "A reason", "invalid_operator")]
    [InlineData(Actor, "", "invalid_operation_reason")]
    [InlineData(Actor, " bad reason ", "invalid_operation_reason")]
    [InlineData(Actor, "reason\nwith newline", "invalid_operation_reason")]
    public async Task AuditInputIsValidatedBeforePersisting(string? actor, string reason, string code)
    {
        var repository = new MemoryRealmRepository();
        var original = Fixtures.Realm(RealmStatus.Ready);
        await repository.TryCreateAsync(original, default);
        Assert.Equal(code, (await Service(repository).RequestRestartAsync(original.RealmId, new(original.Revision, reason), actor, default)).Error);
        Assert.Same(original, await repository.FindAsync(original.RealmId, default));
    }

    [Fact]
    public async Task ManualUpdateUsesOnlyTheApprovedTransitionAndPreservesFrozenConfiguration()
    {
        var repository = new MemoryRealmRepository();
        var original = Fixtures.Realm(RealmStatus.Ready);
        await repository.TryCreateAsync(original, default);
        var options = ManualRelease();
        var service = Service(repository, options);
        var available = service.GetOperationsOptions(original);
        Assert.True(available.CanUpdate);
        Assert.Equal(NextImage, available.UpdateImage);
        Assert.Equal(1, available.UpdateVersion);
        Assert.Equal("approved_image_changed", (await service.RequestUpdateAsync(original.RealmId,
            new(original.Revision, "Update dedicated", Fixtures.Image), Actor, default)).Error);
        var request = new UpdateRealmRequest(original.Revision, "Update dedicated", NextImage);
        var accepted = (await service.RequestUpdateAsync(original.RealmId, request, Actor, default)).Realm!;
        Assert.Equal(NextImage, accepted.Profile.Image);
        Assert.Equal(new RealmImageTransition(1, Fixtures.Image, NextImage), accepted.ImageRelease);
        Assert.Equal(original.Profile.Servers, accepted.Profile.Servers);
        Assert.Equal(original.DeploymentIdentity, accepted.DeploymentIdentity);
        Assert.Equal(RealmStatus.Pending, accepted.Status);
        Assert.Same(accepted, (await service.RequestUpdateAsync(original.RealmId, request, Actor, default)).Realm);
        var operation = Assert.Single(accepted.Operations);
        Assert.Equal("Update", operation.Kind);
        Assert.Equal(Fixtures.Image, operation.PreviousImage);
        Assert.Equal(NextImage, operation.Image);
    }

    [Theory]
    [InlineData("release-absent")]
    [InlineData("topology-changed")]
    [InlineData("target-changed")]
    [InlineData("wrong-previous-image")]
    [InlineData("disabled")]
    public async Task UnsafeOrUnavailableUpdateNeverChangesTheRealm(string scenario)
    {
        var repository = new MemoryRealmRepository();
        var original = Fixtures.Realm(RealmStatus.Ready);
        await repository.TryCreateAsync(original, default);
        var options = ManualRelease();
        if (scenario == "release-absent") options.ImageReleases = [];
        if (scenario == "topology-changed") options.Profiles[0].Servers[0].Args = ["different"];
        if (scenario == "target-changed") options.Targets[0].Namespace = "other-namespace";
        if (scenario == "wrong-previous-image") options.ImageReleases[0].PreviousImage = NextImage;
        if (scenario == "disabled") options.Enabled = false;
        var service = Service(repository, options);
        Assert.False(service.GetOperationsOptions(original).CanUpdate);
        Assert.Null((await service.RequestUpdateAsync(original.RealmId, new(original.Revision, "Update dedicated", NextImage), Actor, default)).Realm);
        Assert.Same(original, await repository.FindAsync(original.RealmId, default));
    }

    [Fact]
    public async Task ManualReleaseWaitsForOperatorButRequestedUpdateReconcilesNormally()
    {
        var repository = new MemoryRealmRepository();
        var original = Fixtures.Realm(RealmStatus.Ready);
        await repository.TryCreateAsync(original, default);
        var options = ManualRelease();
        var deployer = new StubDeployer { Observations = [new("area-west", 1, "192.0.2.1") { ReadyImage = Fixtures.Image }] };
        var reconciler = new RealmReconciler(repository, deployer, Options.Create(options), new TestClock());
        await reconciler.ReconcileAsync(original, default);
        var unchanged = (await repository.FindAsync(original.RealmId, default))!;
        Assert.Equal(Fixtures.Image, unchanged.Profile.Image);
        Assert.Null(unchanged.ImageRelease);
        var requested = (await Service(repository, options).RequestUpdateAsync(original.RealmId,
            new(original.Revision, "Update dedicated", NextImage), Actor, default)).Realm!;
        deployer.Observations = [new("area-west", 1, "192.0.2.1") { ReadyImage = NextImage }];
        await reconciler.ReconcileAsync(requested, default);
        var ready = (await repository.FindAsync(original.RealmId, default))!;
        Assert.Equal(RealmStatus.Ready, ready.Status);
        Assert.Equal(RealmStatus.Ready, Assert.Single(ready.Operations).Status);
        Assert.NotNull(ready.Operations[0].CompletedAt);
    }

    [Fact]
    public async Task NextManualReleaseKeepsThePreviouslyReleasedRealmMonitoredWithoutPromotingIt()
    {
        var repository = new MemoryRealmRepository();
        var current = Fixtures.Realm(RealmStatus.Ready) with
        {
            Profile = Fixtures.Realm().Profile with { Image = NextImage }, ImageRelease = new(1, Fixtures.Image, NextImage)
        };
        await repository.TryCreateAsync(current, default);
        var options = ManualRelease();
        options.ImageReleases[0].Version = 2;
        options.ImageReleases[0].PreviousImage = NextImage;
        options.Profiles[0].Image = Fixtures.Image;
        var deployer = new StubDeployer { Observations = [new("area-west", 0, null)] };
        var service = Service(repository, options);
        Assert.True(service.GetOperationsOptions(current).CanRestart);
        Assert.True(service.GetOperationsOptions(current).CanUpdate);
        await new RealmReconciler(repository, deployer, Options.Create(options), new TestClock()).ReconcileAsync(current, default);
        var observed = (await repository.FindAsync(current.RealmId, default))!;
        Assert.Equal(1, deployer.Calls);
        Assert.Equal(RealmStatus.Deploying, observed.Status);
        Assert.Equal(current.ImageRelease, observed.ImageRelease);
        Assert.Equal(current.Profile.Image, observed.Profile.Image);
        Assert.Equal(current.Revision, observed.Revision);
        Assert.False(service.GetOperationsOptions(observed with { Status = RealmStatus.Failed }).CanUpdate);
    }

    [Fact]
    public async Task FailedUpdateCanRetryTheSameApprovedImageTransition()
    {
        var repository = new MemoryRealmRepository();
        var failed = Fixtures.Realm(RealmStatus.Failed) with
        {
            Profile = Fixtures.Realm().Profile with { Image = NextImage },
            ImageRelease = new(1, Fixtures.Image, NextImage), LastError = "cluster_unavailable"
        };
        await repository.TryCreateAsync(failed, default);
        var service = Service(repository, ManualRelease());
        Assert.True(service.GetOperationsOptions(failed).CanUpdate);
        var accepted = (await service.RequestUpdateAsync(failed.RealmId, new(failed.Revision, "Retry update", NextImage), Actor, default)).Realm!;
        Assert.Equal(failed.ImageRelease, accepted.ImageRelease);
        Assert.Equal(failed.Revision + 1, accepted.Revision);
        Assert.Equal(RealmStatus.Pending, accepted.Status);
        Assert.Null(accepted.LastError);
    }

    [Fact]
    public async Task RestartWaitsForItsOwnReplicaAndKeepsSuccessfulAuditWhenLaterHealthChanges()
    {
        var repository = new MemoryRealmRepository();
        var original = Fixtures.Realm(RealmStatus.Ready);
        await repository.TryCreateAsync(original, default);
        var requested = (await Service(repository).RequestRestartAsync(original.RealmId, new(original.Revision, "Restart now"), Actor, default)).Realm!;
        var deployer = new StubDeployer();
        var reconciler = new RealmReconciler(repository, deployer, Options.Create(Fixtures.Options()), new TestClock());
        await reconciler.ReconcileAsync(requested, default);
        var waiting = (await repository.FindAsync(original.RealmId, default))!;
        Assert.Equal(RealmStatus.Deploying, waiting.Status);
        Assert.Null(waiting.Operations[0].CompletedAt);
        deployer.Observations = [new("area-west", 1, "192.0.2.1") { ReadyRestartRevision = requested.Restart!.Revision }];
        await reconciler.ReconcileAsync(waiting, default);
        var ready = (await repository.FindAsync(original.RealmId, default))!;
        Assert.Equal(RealmStatus.Ready, ready.Operations[0].Status);
        Assert.NotNull(ready.Operations[0].CompletedAt);
        deployer.Observations = [];
        await reconciler.ReconcileAsync(ready, default);
        var unavailable = (await repository.FindAsync(original.RealmId, default))!;
        Assert.Equal(RealmStatus.Deploying, unavailable.Status);
        Assert.Equal(ready.Operations[0], unavailable.Operations[0]);
    }

    [Fact]
    public void LifecycleIntentAndAuditRoundTripThroughMongoDocument()
    {
        var original = Fixtures.Realm() with
        {
            Restart = new(1, 0), Operations = [new("Restart", 1, 0, Actor, "Restart dedicated", new TestClock().Now,
                null, RealmStatus.Pending, null, Fixtures.Image, Fixtures.Image)],
            Servers = [new("area-west", 1, "192.0.2.1") { ReadyRestartRevision = 1 }]
        };
        var restored = BsonSerializer.Deserialize<RealmDocument>(RealmDocument.From(original).ToBson()).ToRecord();
        Assert.Equal(original.Restart, restored.Restart);
        Assert.Equal(original.Operations, restored.Operations);
        Assert.Equal(original.Servers, restored.Servers);
    }

    [Fact]
    public async Task LifecycleEndpointsRequireServiceAuthenticationAndActorAndRejectUnexpectedArguments()
    {
        using var factory = new RealmFactory();
        await factory.Repository.TryCreateAsync(Fixtures.Realm(RealmStatus.Ready), default);
        using var anonymous = factory.Client(key: null);
        Assert.Equal(HttpStatusCode.Forbidden, (await anonymous.PostAsJsonAsync("/admin/realms/eu-one/restart", new RestartRealmRequest(1, "Restart"))).StatusCode);
        using var client = factory.Client();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/admin/realms/eu-one/restart", new RestartRealmRequest(1, "Restart"))).StatusCode);
        client.DefaultRequestHeaders.Add("X-Arkya-Admin-Actor", Actor);
        Assert.True((await client.GetFromJsonAsync<RealmOperationsOptions>("/admin/realms/eu-one/operations-options"))!.CanRestart);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/admin/realms/eu-one/restart", new { expectedRevision = 1, reason = "Restart", command = "arbitrary-shell" })).StatusCode);
        var accepted = await client.PostAsJsonAsync("/admin/realms/eu-one/restart", new RestartRealmRequest(1, "Restart"));
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.Equal(Actor, Assert.Single((await accepted.Content.ReadFromJsonAsync<RealmSummary>())!.Operations).RequestedBy);
    }

    private static RealmService Service(IRealmRepository repository, DeploymentOptions? options = null) =>
        new(repository, Options.Create(options ?? Fixtures.Options()), new TestClock());

    private static DeploymentOptions ManualRelease()
    {
        var options = Fixtures.Options();
        options.Targets[0].RealmId = "eu-one";
        options.Profiles[0].Image = NextImage;
        options.ImageReleases = [new() { RealmId = "eu-one", ProfileId = "island-v1", Version = 1, PreviousImage = Fixtures.Image, Automatic = false }];
        return options;
    }
}
