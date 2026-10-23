using System.Net;
using System.Net.Http.Json;
using Arkya.Realms.Api.Application;
using Arkya.Realms.Api.Contracts;
using Arkya.Realms.Api.Domain;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Options;

namespace Arkya.Realms.Tests;

public sealed class RealmDeletionTests
{
    private const string Actor = "admin:discord:123456789012345678";
    private const string Reason = "Close the test world";
    private static RealmService Service(IRealmRepository repository, bool enabled = true, TestClock? clock = null) =>
        new(repository, Options.Create(Fixtures.Options(enabled)), clock ?? new TestClock());
    private static DeleteRealmRequest Request(RealmRecord realm) => new(realm.Revision, Reason, realm.RealmId);

    [Fact]
    public async Task DraftDeletionWorksWithoutClusterAndDisappearsFromAdminPublicAndStandaloneAdmission()
    {
        using var factory = new RealmFactory(enabled: false);
        var original = Fixtures.Realm(RealmStatus.Draft);
        await factory.Repository.TryCreateAsync(original, default);
        using var client = factory.Client();
        client.DefaultRequestHeaders.Add("X-Arkya-Admin-Actor", Actor);
        using var accepted = await client.PostAsJsonAsync("/admin/realms/eu-one/delete", Request(original));
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.Equal(RealmStatus.Deleted, (await accepted.Content.ReadFromJsonAsync<RealmSummary>())!.Status);
        var deleted = (await factory.Repository.FindAsync(original.RealmId, default))!;
        Assert.Equal(new TestClock().Now, deleted.DeletedAt);
        Assert.Equal(original.DeploymentIdentity, deleted.DeploymentIdentity);
        Assert.Equal(Actor, Assert.Single(deleted.Operations).RequestedBy);
        Assert.Equal(Reason, deleted.Operations[0].Reason);
        Assert.Equal(RealmStatus.Deleted, deleted.Operations[0].Status);
        Assert.NotNull(deleted.Operations[0].CompletedAt);
        Assert.Empty((await client.GetFromJsonAsync<RealmSummary[]>("/admin/realms"))!);
        Assert.Empty((await client.GetFromJsonAsync<PublicRealmSummary[]>("/v1/public/realms"))!);
        foreach (var path in new[] { "/admin/realms/eu-one", "/admin/realms/eu-one/operations-options", "/v1/public/realms/eu-one" })
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/admin/realms/eu-one/delete", Request(original))).StatusCode);
        var catalogue = new GameEntryService(factory.Repository, Options.Create(new GameEntryOptions
        {
            StandaloneEntries = [new() { RealmId = original.RealmId, DisplayName = "Legacy world", Enabled = true,
                ServerId = "area-west", Endpoint = "wss://game.example/" }]
        }));
        Assert.Empty((await catalogue.ListAsync(default)).Realms);
        Assert.Equal("realm_or_target_exists", (await Service(factory.Repository).CreateAsync(
            new(original.RealmId, "Reused identity", original.TargetId, original.Profile.Id), default)).Error);
        Assert.NotNull((await Service(factory.Repository).CreateAsync(
            new("eu-two", "New identity", original.TargetId, original.Profile.Id), default)).Realm);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("incorrect-key")]
    public async Task DeletionRequiresThePrivateAdminKey(string? key)
    {
        using var factory = new RealmFactory();
        var original = Fixtures.Realm(RealmStatus.Draft);
        await factory.Repository.TryCreateAsync(original, default);
        using var client = factory.Client(key);
        client.DefaultRequestHeaders.Add("X-Arkya-Admin-Actor", Actor);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/admin/realms/eu-one/delete", Request(original))).StatusCode);
        Assert.Equal(original, await factory.Repository.FindAsync(original.RealmId, default));
    }

    [Theory]
    [InlineData("", Reason, "eu-one", 1, "invalid_operator")]
    [InlineData(Actor, "", "eu-one", 1, "invalid_operation_reason")]
    [InlineData(Actor, "bad\nreason", "eu-one", 1, "invalid_operation_reason")]
    [InlineData(Actor, Reason, "EU-ONE", 1, "invalid_delete_confirmation")]
    [InlineData(Actor, Reason, "eu-two", 1, "invalid_delete_confirmation")]
    [InlineData(Actor, Reason, "eu-one", -1, "revision_conflict")]
    [InlineData(Actor, Reason, "eu-one", 0, "revision_conflict")]
    public async Task InvalidDeletionNeverChangesState(string actor, string reason, string confirmation, long revision, string error)
    {
        var repository = new MemoryRealmRepository();
        var original = Fixtures.Realm(RealmStatus.Ready);
        await repository.TryCreateAsync(original, default);
        Assert.Equal(error, (await Service(repository).RequestDeletionAsync(original.RealmId,
            new(revision, reason, confirmation), actor, default)).Error);
        Assert.Equal(original, await repository.FindAsync(original.RealmId, default));
    }

    [Theory]
    [InlineData(RealmStatus.Pending)]
    [InlineData(RealmStatus.Deploying)]
    [InlineData(RealmStatus.Deleting)]
    public async Task DeploymentOrDeletionInProgressRejectsAnotherIntent(string status)
    {
        var repository = new MemoryRealmRepository();
        var original = Fixtures.Realm(status);
        await repository.TryCreateAsync(original, default);
        Assert.Equal("deletion_unavailable", (await Service(repository).RequestDeletionAsync(original.RealmId, Request(original), Actor, default)).Error);
        Assert.Equal(original, await repository.FindAsync(original.RealmId, default));
    }

    [Fact]
    public async Task DeployedRealmNeedsCleanupEnabledAndAnApprovedTarget()
    {
        var repository = new MemoryRealmRepository();
        var original = Fixtures.Realm(RealmStatus.Ready);
        await repository.TryCreateAsync(original, default);
        Assert.Equal("deletion_disabled", (await Service(repository, enabled: false).RequestDeletionAsync(original.RealmId, Request(original), Actor, default)).Error);
        var options = Fixtures.Options();
        options.Targets = [];
        var service = new RealmService(repository, Options.Create(options), new TestClock());
        Assert.Equal("target_unavailable", (await service.RequestDeletionAsync(original.RealmId, Request(original), Actor, default)).Error);
    }

    [Fact]
    public async Task DeletionWaitsForWorkloadsThenReleasesTargetAndKeepsAuditIdentity()
    {
        var repository = new MemoryRealmRepository();
        var original = Fixtures.Realm(RealmStatus.Ready);
        await repository.TryCreateAsync(original, default);
        var service = Service(repository);
        var deleting = (await service.RequestDeletionAsync(original.RealmId, Request(original), Actor, default)).Realm!;
        Assert.Equal(RealmStatus.Deleting, deleting.Status);
        Assert.Null(deleting.DeletedAt);
        Assert.Equal(original.Revision + 1, deleting.Revision);
        Assert.Same(deleting, (await service.RequestDeletionAsync(original.RealmId, Request(original), Actor, default)).Realm);
        var deployer = new StubDeployer { DeletionComplete = false };
        var reconciler = new RealmReconciler(repository, deployer, Options.Create(Fixtures.Options()), new TestClock());
        await reconciler.ReconcileAsync(deleting, default);
        var waiting = (await repository.FindAsync(original.RealmId, default))!;
        Assert.Equal(RealmStatus.Deleting, waiting.Status);
        Assert.Null(waiting.DeletedAt);
        Assert.Equal("realm_or_target_exists", (await service.CreateAsync(new("eu-two", "Next world", original.TargetId, original.Profile.Id), default)).Error);
        deployer.DeletionComplete = true;
        await reconciler.ReconcileAsync(waiting, default);
        var deleted = (await repository.FindAsync(original.RealmId, default))!;
        Assert.Equal(RealmStatus.Deleted, deleted.Status);
        Assert.NotNull(deleted.DeletedAt);
        Assert.Equal("Delete", Assert.Single(deleted.Operations).Kind);
        Assert.Equal(RealmStatus.Deleted, deleted.Operations[0].Status);
        Assert.Equal(0, deployer.Calls);
        Assert.Equal(2, deployer.DeleteCalls);
        Assert.NotNull((await service.CreateAsync(new("eu-two", "Next world", original.TargetId, original.Profile.Id), default)).Realm);
        await reconciler.ReconcileAsync(original, default);
        await reconciler.ReconcileAsync(deleted, default);
        Assert.Equal(0, deployer.Calls);
    }

    [Theory]
    [InlineData("resource_protected")]
    [InlineData("resource_ownership_conflict")]
    [InlineData("cluster_access_denied")]
    public async Task PermanentCleanupFailureKeepsReservationAndCanBeExplicitlyRetried(string code)
    {
        var repository = new MemoryRealmRepository();
        var original = Fixtures.Realm(RealmStatus.Failed);
        await repository.TryCreateAsync(original, default);
        var service = Service(repository);
        var deleting = (await service.RequestDeletionAsync(original.RealmId, Request(original), Actor, default)).Realm!;
        var deployer = new StubDeployer { Error = new DeploymentFailure(code) };
        var reconciler = new RealmReconciler(repository, deployer, Options.Create(Fixtures.Options()), new TestClock());
        await reconciler.ReconcileAsync(deleting, default);
        var failed = (await repository.FindAsync(original.RealmId, default))!;
        Assert.Equal(RealmStatus.DeleteFailed, failed.Status);
        Assert.Equal(code, failed.LastError);
        Assert.Null(failed.DeletedAt);
        Assert.Equal("realm_or_target_exists", (await service.CreateAsync(new("eu-two", "Next world", original.TargetId, original.Profile.Id), default)).Error);
        Assert.Equal("deletion_unavailable", (await service.RequestDeploymentAsync(failed.RealmId, failed.Revision, default)).Error);
        var retry = (await service.RequestDeletionAsync(failed.RealmId, Request(failed), Actor, default)).Realm!;
        Assert.Equal(RealmStatus.Deleting, retry.Status);
        Assert.Equal(2, retry.Operations.Length);
        deployer.Error = null;
        await reconciler.ReconcileAsync(retry, default);
        Assert.Equal(RealmStatus.Deleted, (await repository.FindAsync(original.RealmId, default))!.Status);
    }

    [Fact]
    public async Task TransientCleanupErrorsAreSanitizedAndTimeoutRequiresExplicitRetry()
    {
        var repository = new MemoryRealmRepository();
        var clock = new TestClock();
        var original = Fixtures.Realm(RealmStatus.Ready);
        await repository.TryCreateAsync(original, default);
        var deleting = (await Service(repository).RequestDeletionAsync(original.RealmId, Request(original), Actor, default)).Realm!;
        var deployer = new StubDeployer { Error = new HttpRequestException("private-cluster-secret") };
        var reconciler = new RealmReconciler(repository, deployer, Options.Create(Fixtures.Options()), clock);
        await reconciler.ReconcileAsync(deleting, default);
        var retrying = (await repository.FindAsync(original.RealmId, default))!;
        Assert.Equal(RealmStatus.Deleting, retrying.Status);
        Assert.Equal("cluster_unavailable", retrying.LastError);
        clock.Now = clock.Now.AddMinutes(6);
        await reconciler.ReconcileAsync(retrying, default);
        var failed = (await repository.FindAsync(original.RealmId, default))!;
        Assert.Equal(RealmStatus.DeleteFailed, failed.Status);
        Assert.Equal("deletion_timeout", failed.LastError);
        Assert.Equal("deletion_timeout", Assert.Single(failed.Operations).Error);
    }

    [Theory]
    [InlineData(RealmStatus.Deleting)]
    [InlineData(RealmStatus.DeleteFailed)]
    [InlineData(RealmStatus.Deleted)]
    public async Task RemovedRealmCannotServeRuntimeKeysConfigurationOrPlayerAdmission(string status)
    {
        var repository = new MemoryRealmRepository();
        var realm = Fixtures.Realm(status);
        realm = realm with { Profile = realm.Profile with { Servers = [realm.Profile.Servers[0] with
            { Transport = "WebSocket", PublicEndpoint = "wss://game.example/" }] } };
        await repository.TryCreateAsync(realm, default);
        var runtime = new RealmRuntimeOptions { DedicatedKeySeed = new string('k', 32) };
        var commands = new RealmCommandService(repository, new MemoryRealmCommandRepository(), Options.Create(runtime), new TestClock());
        var key = RealmRuntimeKeys.DeriveDedicatedKey(runtime.DedicatedKeySeed, realm.RealmId, realm.Profile.Servers[0].Id);
        Assert.False(await commands.AuthorizeAsync(realm.RealmId, realm.Profile.Servers[0].Id, key, default));
        var configuration = new RealmConfigurationService(repository, new TestClock());
        Assert.Null(await configuration.GetSnapshotAsync(realm.RealmId, default));
        Assert.NotNull((await configuration.UpdateAsync(realm.RealmId, new(0, 2, 2, Reason), Actor, default)).Error);
        var publicRealms = new PublicRealmService(repository, Options.Create(Fixtures.Options()), new TestClock());
        Assert.Empty(await publicRealms.ListAsync(default));
        var admission = new GameEntryService(repository, Options.Create(new GameEntryOptions
        {
            StandaloneEntries = [new() { RealmId = realm.RealmId, DisplayName = "Legacy entry", Enabled = true,
                ServerId = realm.Profile.Servers[0].Id, Endpoint = "wss://legacy.example/" }]
        }));
        Assert.Empty((await admission.ListAsync(default)).Realms);
    }

    [Fact]
    public async Task StaleInflightDeploymentFinishesBeforeCleanupAndCannotRecreateDeletedWorkloads()
    {
        var repository = new MemoryRealmRepository();
        var original = Fixtures.Realm(RealmStatus.Ready);
        await repository.TryCreateAsync(original, default);
        var deployer = new BlockingDeployer();
        var reconciler = new RealmReconciler(repository, deployer, Options.Create(Fixtures.Options()), new TestClock());
        var inflight = reconciler.ReconcileAsync(original, default);
        await deployer.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var deleting = (await Service(repository).RequestDeletionAsync(original.RealmId, Request(original), Actor, default)).Realm!;
        var cleanup = reconciler.ReconcileAsync(deleting, default);
        Assert.False(cleanup.IsCompleted);
        Assert.Equal(0, deployer.DeleteCalls);
        deployer.Release.SetResult();
        await Task.WhenAll(inflight, cleanup).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(RealmStatus.Deleted, (await repository.FindAsync(original.RealmId, default))!.Status);
        await reconciler.ReconcileAsync(original, default);
        Assert.Equal(1, deployer.ReconcileCalls);
        Assert.Equal(1, deployer.DeleteCalls);
    }

    private sealed class BlockingDeployer : IRealmDeployer
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ReconcileCalls { get; private set; }
        public int DeleteCalls { get; private set; }
        public async Task<ServerObservation[]> ReconcileAsync(RealmRecord realm, CancellationToken cancellationToken)
        {
            ReconcileCalls++;
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return [new("area-west", 1, "192.0.2.1")];
        }
        public Task<bool> DeleteAsync(RealmRecord realm, CancellationToken cancellationToken)
        { DeleteCalls++; return Task.FromResult(true); }
    }
}
