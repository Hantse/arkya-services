using Arkya.Inference.Api.Application;
using Arkya.Inference.Api.Domain;
using Arkya.Inference.Api.Infrastructure;
using Arkya.ServiceDefaults.Inference;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arkya.Inference.Tests;

internal sealed class TestClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}

internal sealed class MemoryInferenceRepository : IInferenceRepository
{
    private readonly Dictionary<string, InferenceAgentRecord> agents = [];
    private readonly Dictionary<string, string[]> revokedTokenHashes = [];
    private readonly object gate = new();
    public int ReplacementConflictsRemaining { get; set; }
    public int Conflicts { get; private set; }
    public bool Unavailable { get; set; }
    public int CreationFailuresRemaining { get; set; }
    public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(!Unavailable);
    public Task<IReadOnlyList<InferenceAgentRecord>> ListAsync(CancellationToken cancellationToken)
    { lock (gate) return Task.FromResult<IReadOnlyList<InferenceAgentRecord>>(agents.Values.OrderBy(value => value.AgentId).ToArray()); }
    public Task<InferenceAgentRecord?> FindAsync(string agentId, CancellationToken cancellationToken)
    { lock (gate) return Task.FromResult(agents.GetValueOrDefault(agentId)); }
    public Task<bool> TryCreateAsync(InferenceAgentRecord agent, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (CreationFailuresRemaining > 0)
            {
                CreationFailuresRemaining--;
                throw new TimeoutException("Injected interrupted enrollment.");
            }
            if (agents.ContainsKey(agent.AgentId)) return Task.FromResult(false);
            var revoked = revokedTokenHashes.GetValueOrDefault(agent.AgentId) ?? [];
            if (revoked.Length >= MongoInferenceRepository.MaxRetiredCredentials || revoked.Contains(agent.TokenHash))
                return Task.FromResult(false);
            agents.Add(agent.AgentId, agent);
            return Task.FromResult(true);
        }
    }
    public async Task<bool> TryReplaceAsync(InferenceAgentRecord agent, long expectedVersion, CancellationToken cancellationToken)
    {
        await Task.Yield();
        lock (gate)
        {
            if (ReplacementConflictsRemaining > 0)
            {
                ReplacementConflictsRemaining--;
                Conflicts++;
                return false;
            }
            if (!agents.TryGetValue(agent.AgentId, out var current) || current.Version != expectedVersion ||
                current.TokenHash != agent.TokenHash)
            {
                Conflicts++;
                return false;
            }
            agents[agent.AgentId] = agent;
            return true;
        }
    }
    public Task<bool> TryDeleteAsync(string agentId, long expectedVersion, string expectedTokenHash, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (!agents.TryGetValue(agentId, out var current) || current.Version != expectedVersion ||
                current.TokenHash != expectedTokenHash) return Task.FromResult(false);
            agents.Remove(agentId);
            revokedTokenHashes[agentId] = [.. revokedTokenHashes.GetValueOrDefault(agentId) ?? [], expectedTokenHash];
            return Task.FromResult(true);
        }
    }
}

internal sealed class PausedCreationRepository(IInferenceRepository inner) : IInferenceRepository
{
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task InitializeAsync(CancellationToken cancellationToken) => inner.InitializeAsync(cancellationToken);
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => inner.IsAvailableAsync(cancellationToken);
    public Task<IReadOnlyList<InferenceAgentRecord>> ListAsync(CancellationToken cancellationToken) => inner.ListAsync(cancellationToken);
    public Task<InferenceAgentRecord?> FindAsync(string agentId, CancellationToken cancellationToken) => inner.FindAsync(agentId, cancellationToken);
    public async Task<bool> TryCreateAsync(InferenceAgentRecord agent, CancellationToken cancellationToken)
    {
        Entered.SetResult();
        await Resume.Task.WaitAsync(cancellationToken);
        return await inner.TryCreateAsync(agent, cancellationToken);
    }
    public Task<bool> TryReplaceAsync(InferenceAgentRecord agent, long expectedVersion, CancellationToken cancellationToken) =>
        inner.TryReplaceAsync(agent, expectedVersion, cancellationToken);
    public Task<bool> TryDeleteAsync(string agentId, long expectedVersion, string expectedTokenHash, CancellationToken cancellationToken) =>
        inner.TryDeleteAsync(agentId, expectedVersion, expectedTokenHash, cancellationToken);
}

internal sealed class MemoryEnrollmentGrantRepository : IEnrollmentGrantRepository
{
    private readonly Dictionary<string, EnrollmentGrantRecord> grants = [];
    private readonly object gate = new();
    public bool RevokeUnavailable { get; set; }
    public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<EnrollmentGrantRecord?> FindAsync(string tokenHash, CancellationToken cancellationToken)
    { lock (gate) return Task.FromResult(grants.GetValueOrDefault(tokenHash)); }
    public Task<bool> TryCreateAsync(EnrollmentGrantRecord grant, CancellationToken cancellationToken)
    { lock (gate) return Task.FromResult(grants.TryAdd(grant.TokenHash, grant)); }
    public async Task<bool> TryBindAsync(string tokenHash, string agentId, string agentTokenHash, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await Task.Yield();
        lock (gate)
        {
            if (!grants.TryGetValue(tokenHash, out var grant) || grant.ExpiresAt <= now || grant.Revoked ||
                (grant.BindingAgentId is not null && (grant.BindingAgentId != agentId || grant.BindingTokenHash != agentTokenHash)))
                return false;
            grants[tokenHash] = grant with { BindingAgentId = agentId, BindingTokenHash = agentTokenHash };
            return true;
        }
    }
    public Task RevokeForAgentAsync(string agentId, string agentTokenHash, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (RevokeUnavailable) throw new TimeoutException("Injected unavailable grant store.");
            foreach (var grant in grants.Values.Where(value => value.BindingAgentId == agentId && value.BindingTokenHash == agentTokenHash).ToArray())
                grants[grant.TokenHash] = grant with { Revoked = true };
            return Task.CompletedTask;
        }
    }
}

internal sealed class InferenceFactory(bool production = false) : WebApplicationFactory<Arkya.Inference.Api.Program>
{
    public const string AdminKey = "inference-administrator-test-key-123456789";
    public MemoryInferenceRepository Repository { get; } = new();
    public MemoryEnrollmentGrantRepository Grants { get; } = new();
    public TestClock Clock { get; } = new();
    public Dictionary<string, string?> Settings { get; } = [];
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(production ? "Production" : "Development");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["InferenceAdmin:Key"] = AdminKey,
                ["Mongo:DatabaseName"] = "arkya_inference_tests"
            });
            configuration.AddInMemoryCollection(Settings);
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IInferenceRepository>();
            services.AddSingleton<IInferenceRepository>(Repository);
            services.RemoveAll<IEnrollmentGrantRepository>();
            services.AddSingleton<IEnrollmentGrantRepository>(Grants);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }
    public HttpClient Client(string? key = AdminKey, string scheme = "https")
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
            { BaseAddress = new Uri($"{scheme}://localhost"), AllowAutoRedirect = false });
        if (key is not null) client.DefaultRequestHeaders.Add(InferenceAdminKeyValidator.HeaderName, key);
        return client;
    }
}

internal static class Fixtures
{
    public static CreateInferenceAgentRequest Create(string id = "cpu-one", int workers = 3, int maxWorkers = 6) =>
        new(id, "CPU one", workers, 4, maxWorkers);
    public static InferenceHeartbeatRequest Heartbeat(string agentId, params InferenceWorkerObserved[] workers) =>
        new(agentId, "WINDOWS-CPU", "1.0.0", 24, 131072, workers, 8767, null);
    public static InferenceWorkerObserved Observe(InferenceWorkerDesired worker, int port = 11435) =>
        new(worker.WorkerId, "ready", 1234, port, worker.Threads, null, worker.ModelId);
}
