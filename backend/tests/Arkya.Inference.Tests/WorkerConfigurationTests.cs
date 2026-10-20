using System.Net;
using System.Net.Http.Json;
using Arkya.Inference.Api.Application;
using Arkya.ServiceDefaults.Inference;
using Microsoft.Extensions.Options;

namespace Arkya.Inference.Tests;

public sealed class WorkerConfigurationTests
{
    private const string Agents = "/admin/inference/agents";

    [Fact]
    public async Task CatalogAndWorkerUpdatesRequireAdminAndPreserveWorkerIdentity()
    {
        using var factory = new InferenceFactory();
        using var admin = factory.Client();
        using var anonymous = factory.Client(null);
        Assert.Equal(HttpStatusCode.Forbidden, (await anonymous.GetAsync("/admin/inference/models")).StatusCode);
        var catalog = (await admin.GetFromJsonAsync<InferenceModelOption[]>("/admin/inference/models"))!;
        Assert.Equal(InferenceModels.Catalog, catalog);
        var created = (await (await admin.PostAsJsonAsync(Agents, Fixtures.Create() with { ModelId = "qwen35-08b" }))
            .Content.ReadFromJsonAsync<CreateInferenceAgentResponse>())!;
        Assert.All(created.Agent.DesiredWorkers, worker => Assert.Equal("qwen35-08b", worker.ModelId));
        var worker = created.Agent.DesiredWorkers[0];
        var path = Agents + "/cpu-one/workers/" + worker.WorkerId;
        Assert.Equal(HttpStatusCode.Forbidden, (await anonymous.PatchAsJsonAsync(path, new UpdateInferenceWorkerRequest(3, "lfm"))).StatusCode);
        var changed = (await (await admin.PatchAsJsonAsync(path, new UpdateInferenceWorkerRequest(3, "lfm")))
            .Content.ReadFromJsonAsync<InferenceAgentSummary>())!;
        Assert.Equal(created.Agent.Revision + 1, changed.Revision);
        Assert.Equal(worker.WorkerId, changed.DesiredWorkers[0].WorkerId);
        Assert.Equal(3, changed.DesiredWorkers[0].Threads);
        Assert.Equal("lfm", changed.DesiredWorkers[0].ModelId);
        Assert.Equal(created.Agent.DesiredWorkers[1], changed.DesiredWorkers[1]);
        var retry = (await (await admin.PatchAsJsonAsync(path, new UpdateInferenceWorkerRequest(3, "lfm")))
            .Content.ReadFromJsonAsync<InferenceAgentSummary>())!;
        Assert.Equal(changed.Revision, retry.Revision);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PatchAsJsonAsync(Agents + "/cpu-one/workers/" + Guid.NewGuid().ToString("N"),
            new UpdateInferenceWorkerRequest(3, "lfm"))).StatusCode);
        var added = (await (await admin.PostAsJsonAsync(Agents + "/cpu-one/workers", new AddInferenceWorkerRequest(2, "qwen35-08b")))
            .Content.ReadFromJsonAsync<InferenceAgentSummary>())!;
        Assert.Equal("qwen35-08b", added.DesiredWorkers[^1].ModelId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://untrusted/model")]
    [InlineData("LFM")]
    [InlineData("llama3")]
    public async Task ModelNamesAreAllowlistedForCreationUpdatesAndObservedState(string? model)
    {
        using var factory = new InferenceFactory();
        using var admin = factory.Client();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync(Agents, Fixtures.Create() with { ModelId = model! })).StatusCode);
        var created = (await (await admin.PostAsJsonAsync(Agents, Fixtures.Create()))
            .Content.ReadFromJsonAsync<CreateInferenceAgentResponse>())!;
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(Agents + "/cpu-one/workers", new AddInferenceWorkerRequest(4, model!))).StatusCode);
        var worker = created.Agent.DesiredWorkers[0];
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PatchAsJsonAsync(Agents + "/cpu-one/workers/" + worker.WorkerId,
            new UpdateInferenceWorkerRequest(4, model!))).StatusCode);
        using var device = factory.Client(null);
        device.DefaultRequestHeaders.Authorization = new("Bearer", created.AgentToken);
        Assert.Equal(HttpStatusCode.BadRequest, (await device.PostAsJsonAsync("/agent/inference/heartbeat",
            Fixtures.Heartbeat("cpu-one", Fixtures.Observe(worker) with { ModelId = model! }))).StatusCode);
        Assert.Null((await factory.Repository.FindAsync("cpu-one", default))!.LastSeenAt);
        Assert.Equal(1, (await factory.Repository.FindAsync("cpu-one", default))!.Revision);
    }

    [Fact]
    public async Task ThreadDecreaseReservesPreviousDesiredPeakUntilAppliedAndObserved()
    {
        var repository = new MemoryInferenceRepository();
        var service = new InferenceService(repository, Options.Create(new InferenceOptions { MaxTotalThreads = 12 }),
            new TestClock(), new MemoryEnrollmentGrantRepository());
        var created = (await service.CreateAsync(Fixtures.Create(workers: 1) with { ThreadsPerWorker = 8 }, default)).Value!;
        var worker = created.Agent.DesiredWorkers.Single();
        var changed = (await service.UpdateWorkerAsync("cpu-one", worker.WorkerId, new(4, "qwen35-08b"), default)).Value!;
        Assert.Equal("worker_limit", (await service.AddWorkerAsync("cpu-one", new(8), default)).Error);
        await service.HeartbeatAsync(Fixtures.Heartbeat("cpu-one") with { AppliedRevision = 1 }, created.AgentToken, default);
        Assert.Equal("worker_limit", (await service.AddWorkerAsync("cpu-one", new(8), default)).Error);
        // Acknowledged configuration can coexist with the old process while it drains.
        await service.HeartbeatAsync(Fixtures.Heartbeat("cpu-one", Fixtures.Observe(worker)) with { AppliedRevision = 2 }, created.AgentToken, default);
        Assert.Equal("worker_limit", (await service.AddWorkerAsync("cpu-one", new(8), default)).Error);
        await service.HeartbeatAsync(Fixtures.Heartbeat("cpu-one") with { AppliedRevision = 1 }, created.AgentToken, default);
        Assert.Equal("worker_limit", (await service.AddWorkerAsync("cpu-one", new(8), default)).Error);
        await service.HeartbeatAsync(Fixtures.Heartbeat("cpu-one", Fixtures.Observe(changed.DesiredWorkers.Single())) with { AppliedRevision = 2 }, created.AgentToken, default);
        Assert.Null((await service.AddWorkerAsync("cpu-one", new(8), default)).Error);
    }

    [Fact]
    public async Task UpdatesAndRemovalRespectPendingPeaksAndConcurrentHeartbeats()
    {
        var repository = new MemoryInferenceRepository { ReplacementConflictsRemaining = 2 };
        var service = new InferenceService(repository, Options.Create(new InferenceOptions { MaxTotalThreads = 12 }),
            new TestClock(), new MemoryEnrollmentGrantRepository());
        var created = (await service.CreateAsync(Fixtures.Create(workers: 2), default)).Value!;
        var first = created.Agent.DesiredWorkers[0];
        var update = service.UpdateWorkerAsync("cpu-one", first.WorkerId, new(8, "qwen35-08b"), default);
        var heartbeat = service.HeartbeatAsync(Fixtures.Heartbeat("cpu-one", Fixtures.Observe(first)), created.AgentToken, default);
        await Task.WhenAll(update, heartbeat);
        Assert.Null((await update).Error);
        Assert.Null((await heartbeat).Error);
        Assert.Equal("worker_limit", (await service.UpdateWorkerAsync("cpu-one", created.Agent.DesiredWorkers[1].WorkerId, new(5, "lfm"), default)).Error);
        Assert.Equal("invalid_worker", (await service.UpdateWorkerAsync("cpu-one", first.WorkerId, new(25, "lfm"), default)).Error);
        Assert.Equal("invalid_worker", (await service.UpdateWorkerAsync("cpu-one", first.WorkerId, new(0, "lfm"), default)).Error);
        await service.UpdateWorkerAsync("cpu-one", first.WorkerId, new(2, "lfm"), default);
        await service.RemoveWorkerAsync("cpu-one", first.WorkerId, default);
        Assert.Equal("worker_limit", (await service.AddWorkerAsync("cpu-one", new(1), default)).Error);
        var current = (await repository.FindAsync("cpu-one", default))!;
        Assert.Equal(4, current.Revision);
        await service.HeartbeatAsync(Fixtures.Heartbeat("cpu-one") with { AppliedRevision = current.Revision }, created.AgentToken, default);
        Assert.Null((await service.AddWorkerAsync("cpu-one", new(8), default)).Error);
    }
}
