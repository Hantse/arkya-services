using Arkya.Inference.Api.Application;
using Arkya.Inference.Api.Infrastructure;
using Arkya.ServiceDefaults.Inference;
using Microsoft.Extensions.Options;

namespace Arkya.Inference.Tests;

public sealed class InferenceServiceTests
{
    [Fact]
    public async Task ConcurrentAddsNeverLoseWorkersOrExceedConfiguredCapacity()
    {
        var repository = new MemoryInferenceRepository();
        var service = new InferenceService(repository, Options.Create(new InferenceOptions()), new TestClock(), new MemoryEnrollmentGrantRepository());
        await service.CreateAsync(Fixtures.Create(workers: 0), default);
        var results = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => service.AddWorkerAsync("cpu-one", new(4), default)));
        Assert.Equal(6, results.Count(value => value.Error is null));
        Assert.All(results.Where(value => value.Error is not null), value => Assert.Equal("worker_limit", value.Error));
        var current = (await repository.FindAsync("cpu-one", default))!;
        Assert.Equal(6, current.DesiredWorkers.Length);
        Assert.Equal(6, current.DesiredWorkers.Select(value => value.WorkerId).Distinct().Count());
        Assert.Equal(7, current.Revision);
        Assert.Equal(7, current.Version);
    }

    [Fact]
    public async Task HeartbeatsAndOperatorChangesRetryWithoutOverwritingEachOther()
    {
        var repository = new MemoryInferenceRepository { ReplacementConflictsRemaining = 3 };
        var service = new InferenceService(repository, Options.Create(new InferenceOptions()), new TestClock(), new MemoryEnrollmentGrantRepository());
        var created = (await service.CreateAsync(Fixtures.Create(workers: 0), default)).Value!;
        var tasks = new List<Task>
        {
            service.AddWorkerAsync("cpu-one", new(3), default),
            service.AddWorkerAsync("cpu-one", new(4), default)
        };
        var heartbeat = service.HeartbeatAsync(Fixtures.Heartbeat("cpu-one"), created.AgentToken, default);
        tasks.Add(heartbeat);
        await Task.WhenAll(tasks);
        Assert.Null((await heartbeat).Error);
        var current = (await repository.FindAsync("cpu-one", default))!;
        Assert.Equal(2, current.DesiredWorkers.Length);
        Assert.Equal(3, current.Revision);
        Assert.Equal(4, current.Version);
        Assert.NotNull(current.LastSeenAt);
        Assert.True(repository.Conflicts >= 3);
    }

    [Fact]
    public async Task ConcurrentRemovalIsIdempotentAndUsesOneRevision()
    {
        var repository = new MemoryInferenceRepository();
        var service = new InferenceService(repository, Options.Create(new InferenceOptions()), new TestClock(), new MemoryEnrollmentGrantRepository());
        var created = (await service.CreateAsync(Fixtures.Create(workers: 1), default)).Value!;
        var worker = created.Agent.DesiredWorkers.Single();
        var results = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => service.RemoveWorkerAsync("cpu-one", worker.WorkerId, default)));
        Assert.All(results, value => Assert.Null(value.Error));
        var current = (await repository.FindAsync("cpu-one", default))!;
        Assert.Equal(2, current.Revision);
        Assert.Empty(current.DesiredWorkers);
        Assert.Single(current.RetiredWorkers);
    }

    [Fact]
    public async Task ThreadBudgetCountsWorkersStillStopping()
    {
        var repository = new MemoryInferenceRepository();
        var service = new InferenceService(repository, Options.Create(new InferenceOptions { MaxTotalThreads = 12 }), new TestClock(), new MemoryEnrollmentGrantRepository());
        var created = (await service.CreateAsync(Fixtures.Create(), default)).Value!;
        await service.RemoveWorkerAsync("cpu-one", created.Agent.DesiredWorkers[0].WorkerId, default);
        Assert.Equal("worker_limit", (await service.AddWorkerAsync("cpu-one", new(1), default)).Error);
        await service.HeartbeatAsync(Fixtures.Heartbeat("cpu-one") with { AppliedRevision = 2 }, created.AgentToken, default);
        Assert.Null((await service.AddWorkerAsync("cpu-one", new(4), default)).Error);
        Assert.Equal("worker_limit", (await service.AddWorkerAsync("cpu-one", new(1), default)).Error);
    }

    [Fact]
    public async Task RepeatedStorageContentionReturnsConflictWithoutPartialMutation()
    {
        var repository = new MemoryInferenceRepository { ReplacementConflictsRemaining = 100 };
        var service = new InferenceService(repository, Options.Create(new InferenceOptions()), new TestClock(), new MemoryEnrollmentGrantRepository());
        await service.CreateAsync(Fixtures.Create(workers: 0), default);
        Assert.Equal("concurrent_update", (await service.AddWorkerAsync("cpu-one", new(4), default)).Error);
        Assert.Empty((await repository.FindAsync("cpu-one", default))!.DesiredWorkers);
    }

    [Fact]
    public async Task LoweredResourceLimitsDoNotPreventReportingExistingWorkers()
    {
        var repository = new MemoryInferenceRepository();
        var initial = new InferenceService(repository, Options.Create(new InferenceOptions()), new TestClock(), new MemoryEnrollmentGrantRepository());
        var created = (await initial.CreateAsync(Fixtures.Create(), default)).Value!;
        var tightened = new InferenceService(repository, Options.Create(new InferenceOptions
            { MaxWorkers = 1, MaxThreadsPerWorker = 1 }), new TestClock(), new MemoryEnrollmentGrantRepository());
        var observed = created.Agent.DesiredWorkers.Select((worker, index) => Fixtures.Observe(worker, 11435 + index)).ToArray();
        Assert.Null((await tightened.HeartbeatAsync(Fixtures.Heartbeat("cpu-one", observed), created.AgentToken, default)).Error);
        Assert.Equal("worker_limit", (await tightened.AddWorkerAsync("cpu-one", new(1), default)).Error);
    }

    [Fact]
    public async Task CommandsFromDeletedIncarnationCannotOverwriteOrDeleteRecreatedAgent()
    {
        var repository = new MemoryInferenceRepository();
        var service = new InferenceService(repository, Options.Create(new InferenceOptions()), new TestClock(), new MemoryEnrollmentGrantRepository());
        await service.CreateAsync(Fixtures.Create(workers: 0), default);
        var original = (await repository.FindAsync("cpu-one", default))!;
        Assert.Null(await service.DeleteAsync("cpu-one", default));
        var replacement = (await service.CreateAsync(Fixtures.Create(workers: 0), default)).Value!;
        Assert.False(await repository.TryReplaceAsync(original with { Version = 2 }, 1, default));
        Assert.False(await repository.TryDeleteAsync("cpu-one", 1, original.TokenHash, default));
        Assert.Equal(AgentCredentials.Hash(replacement.AgentToken), (await repository.FindAsync("cpu-one", default))!.TokenHash);
    }

    [Fact]
    public void OptionsAndCredentialsFailClosed()
    {
        Assert.True(new InferenceOptions().IsValid());
        Assert.False(new InferenceOptions { MaxWorkers = 13 }.IsValid());
        Assert.False(new InferenceOptions { MaxThreadsPerWorker = 25 }.IsValid());
        Assert.False(new InferenceOptions { MaxTotalThreads = 0 }.IsValid());
        Assert.False(new InferenceOptions { OfflineAfterSeconds = 2 }.IsValid());
        Assert.False(AgentCredentials.Matches(null, ""));
        Assert.False(AgentCredentials.Matches(new string('a', 64), "invalid"));
        Assert.False(new InferenceAdminKeyValidator(Options.Create(new InferenceAdminOptions())).IsValid(""));
        var token = AgentCredentials.Create();
        Assert.True(AgentCredentials.Matches(token, AgentCredentials.Hash(token)));
        Assert.False(AgentCredentials.Matches(AgentCredentials.Create(), AgentCredentials.Hash(token)));
    }
}
