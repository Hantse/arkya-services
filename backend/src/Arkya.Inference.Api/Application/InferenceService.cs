using Arkya.Inference.Api.Domain;
using Arkya.Inference.Api.Infrastructure;
using Arkya.ServiceDefaults.Inference;
using Microsoft.Extensions.Options;

namespace Arkya.Inference.Api.Application;

public sealed record InferenceResult<T>(T? Value, string? Error) where T : class;

public sealed class InferenceService(IInferenceRepository repository, IOptions<InferenceOptions> settings,
    TimeProvider clock, IEnrollmentGrantRepository grants)
{
    private const int MaxAttempts = 16;
    private readonly InferenceOptions options = settings.Value;
    private static readonly HashSet<string> WorkerStates = new(StringComparer.Ordinal)
    {
        "starting", "loading", "ready", "busy", "stopping", "stopped", "error",
        "offline", "failed", "running", "unhealthy"
    };

    public async Task<InferenceAgentSummary[]> ListAsync(CancellationToken cancellationToken) =>
        (await repository.ListAsync(cancellationToken)).Select(Summary).ToArray();

    public async Task<InferenceResult<InferenceAgentSummary>> GetAsync(string agentId, CancellationToken cancellationToken)
    {
        if (!ValidAgentId(agentId)) return new(null, "invalid_agent");
        var agent = await repository.FindAsync(agentId, cancellationToken);
        return agent is null ? new(null, "agent_not_found") : new(Summary(agent), null);
    }

    public async Task<InferenceResult<CreateInferenceAgentResponse>> CreateAsync(
        CreateInferenceAgentRequest request, CancellationToken cancellationToken)
    {
        var token = AgentCredentials.Create();
        var result = await CreateWithTokenAsync(request, token, cancellationToken);
        return result.Value is { } summary ? new(new(summary, token), null) : new(null, result.Error);
    }

    internal bool ValidCreation(CreateInferenceAgentRequest request) =>
        ValidAgentId(request.AgentId) && ValidText(request.DisplayName, 80) &&
        request.MaxWorkers >= 1 && request.MaxWorkers <= options.MaxWorkers &&
        request.InitialWorkers >= 0 && request.InitialWorkers <= request.MaxWorkers &&
        ValidThreads(request.ThreadsPerWorker) && InferenceModels.IsAllowed(request.ModelId) &&
        (long)request.InitialWorkers * request.ThreadsPerWorker <= options.MaxTotalThreads;

    internal async Task<InferenceResult<InferenceAgentSummary>> CreateWithTokenAsync(
        CreateInferenceAgentRequest request, string token, CancellationToken cancellationToken)
    {
        if (!ValidCreation(request) || !AgentCredentials.IsValidToken(token)) return new(null, "invalid_agent");
        var now = clock.GetUtcNow();
        var workers = Enumerable.Range(0, request.InitialWorkers)
            .Select(_ => NewWorker(request.ThreadsPerWorker, request.ModelId)).ToArray();
        var agent = new InferenceAgentRecord(request.AgentId, Redact(request.DisplayName.Trim(), token)!, AgentCredentials.Hash(token),
            1, 1, 0, request.MaxWorkers, request.ThreadsPerWorker, now, now, null,
            null, null, null, null, null, null, workers, [], [], []);
        return await repository.TryCreateAsync(agent, cancellationToken)
            ? new(Summary(agent), null) : new(null, "agent_exists");
    }

    public async Task<InferenceResult<InferenceAgentSummary>> AddWorkerAsync(
        string agentId, AddInferenceWorkerRequest request, CancellationToken cancellationToken)
    {
        if (!ValidAgentId(agentId) || !ValidThreads(request.Threads) || !InferenceModels.IsAllowed(request.ModelId))
            return new(null, "invalid_worker");
        // Keep the same identity across storage retries for this command.
        var worker = NewWorker(request.Threads, request.ModelId);
        string? incarnation = null;
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var agent = await repository.FindAsync(agentId, cancellationToken);
            if (agent is null) return new(null, "agent_not_found");
            if (incarnation is not null && incarnation != agent.TokenHash) return new(null, "concurrent_update");
            incarnation = agent.TokenHash;
            if (agent.DesiredWorkers.Length + agent.RetiredWorkers.Length >= Math.Min(agent.MaxWorkers, options.MaxWorkers) ||
                ThreadBudget(agent, [.. agent.DesiredWorkers, worker]) > options.MaxTotalThreads)
                return new(null, "worker_limit");
            var updated = agent with
            {
                DesiredWorkers = [.. agent.DesiredWorkers, worker], Revision = agent.Revision + 1,
                Version = agent.Version + 1, DesiredUpdatedAt = clock.GetUtcNow()
            };
            if (await repository.TryReplaceAsync(updated, agent.Version, cancellationToken))
                return new(Summary(updated), null);
        }
        return new(null, "concurrent_update");
    }

    public async Task<InferenceResult<InferenceAgentSummary>> UpdateWorkerAsync(
        string agentId, string workerId, UpdateInferenceWorkerRequest request, CancellationToken cancellationToken)
    {
        if (!ValidAgentId(agentId) || !ValidWorkerId(workerId) || !ValidThreads(request.Threads) ||
            !InferenceModels.IsAllowed(request.ModelId)) return new(null, "invalid_worker");
        string? incarnation = null;
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var agent = await repository.FindAsync(agentId, cancellationToken);
            if (agent is null) return new(null, "agent_not_found");
            if (incarnation is not null && incarnation != agent.TokenHash) return new(null, "concurrent_update");
            incarnation = agent.TokenHash;
            var previous = agent.DesiredWorkers.FirstOrDefault(value => value.WorkerId == workerId);
            if (previous is null) return new(null, "worker_not_found");
            if (previous.Threads == request.Threads && previous.ModelId == request.ModelId)
                return new(Summary(agent), null);
            var desired = agent.DesiredWorkers.Select(value => value.WorkerId == workerId
                ? value with { Threads = request.Threads, ModelId = request.ModelId } : value).ToArray();
            var reservations = MergeReservations(agent.ThreadReservations,
                [new(workerId, Math.Max(previous.Threads, request.Threads))]);
            if (ThreadBudget(agent, desired, reservations) > options.MaxTotalThreads)
                return new(null, "worker_limit");
            var updated = agent with
            {
                DesiredWorkers = desired, ThreadReservations = reservations,
                Revision = agent.Revision + 1, Version = agent.Version + 1, DesiredUpdatedAt = clock.GetUtcNow()
            };
            if (await repository.TryReplaceAsync(updated, agent.Version, cancellationToken))
                return new(Summary(updated), null);
        }
        return new(null, "concurrent_update");
    }

    public async Task<InferenceResult<InferenceAgentSummary>> RemoveWorkerAsync(
        string agentId, string workerId, CancellationToken cancellationToken)
    {
        if (!ValidAgentId(agentId) || !ValidWorkerId(workerId)) return new(null, "invalid_worker");
        string? incarnation = null;
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var agent = await repository.FindAsync(agentId, cancellationToken);
            if (agent is null) return new(null, "agent_not_found");
            if (incarnation is not null && incarnation != agent.TokenHash) return new(null, "concurrent_update");
            incarnation = agent.TokenHash;
            if (!agent.DesiredWorkers.Any(value => value.WorkerId == workerId))
                return new(Summary(agent), null);
            var updated = agent with
            {
                DesiredWorkers = agent.DesiredWorkers.Where(value => value.WorkerId != workerId).ToArray(),
                RetiredWorkers = [.. agent.RetiredWorkers, agent.DesiredWorkers.Single(value => value.WorkerId == workerId)],
                Revision = agent.Revision + 1, Version = agent.Version + 1, DesiredUpdatedAt = clock.GetUtcNow()
            };
            if (await repository.TryReplaceAsync(updated, agent.Version, cancellationToken))
                return new(Summary(updated), null);
        }
        return new(null, "concurrent_update");
    }

    public async Task<string?> DeleteAsync(string agentId, CancellationToken cancellationToken)
    {
        if (!ValidAgentId(agentId)) return "invalid_agent";
        string? incarnation = null;
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var agent = await repository.FindAsync(agentId, cancellationToken);
            if (agent is null) return null;
            if (incarnation is not null && incarnation != agent.TokenHash) return "concurrent_update";
            incarnation = agent.TokenHash;
            // Revocation is not a remote process stop: wait for an observed drain.
            if (agent.DesiredWorkers.Length != 0 || agent.ObservedWorkers.Length != 0 ||
                (agent.LastSeenAt is not null && (!IsOnline(agent) || agent.ObservedRevision != agent.Revision)))
                return "agent_not_drained";
            await grants.RevokeForAgentAsync(agentId, agent.TokenHash, cancellationToken);
            if (await repository.TryDeleteAsync(agentId, agent.Version, agent.TokenHash, cancellationToken)) return null;
        }
        return "concurrent_update";
    }

    public async Task<InferenceResult<InferenceHeartbeatResponse>> HeartbeatAsync(
        InferenceHeartbeatRequest request, string? token, CancellationToken cancellationToken)
    {
        if (!ValidAgentId(request.AgentId)) return new(null, "unauthorized_agent");
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var agent = await repository.FindAsync(request.AgentId, cancellationToken);
            if (agent is null || !AgentCredentials.Matches(token, agent.TokenHash))
                return new(null, "unauthorized_agent");
            if (!ValidHeartbeat(request, agent)) return new(null, "invalid_heartbeat");
            var observedIds = request.Workers.Select(value => value.WorkerId).ToHashSet(StringComparer.Ordinal);
            var updated = agent with
            {
                Version = agent.Version + 1, ObservedRevision = request.AppliedRevision, LastSeenAt = clock.GetUtcNow(),
                MachineName = Redact(request.MachineName, token!),
                AgentVersion = Redact(request.Version, token!), LogicalProcessors = request.LogicalProcessors,
                TotalMemoryMb = request.TotalMemoryMb, DemoPort = request.DemoPort,
                LastError = Redact(request.LastError, token!),
                ObservedWorkers = request.Workers.Select(value => value with { LastError = Redact(value.LastError, token!) }).ToArray(),
                RetiredWorkers = request.AppliedRevision == agent.Revision
                    ? agent.RetiredWorkers.Where(value => observedIds.Contains(value.WorkerId)).ToArray()
                    : agent.RetiredWorkers,
                ThreadReservations = request.AppliedRevision == agent.Revision ? [] :
                    MergeReservations(agent.ThreadReservations,
                        agent.ObservedWorkers.Select(value => new InferenceWorkerThreadReservation(value.WorkerId, value.Threads)))
            };
            if (await repository.TryReplaceAsync(updated, agent.Version, cancellationToken))
                return new(new(agent.AgentId, agent.Revision, agent.DesiredWorkers, options.HeartbeatSeconds), null);
        }
        return new(null, "concurrent_update");
    }

    private bool ValidHeartbeat(InferenceHeartbeatRequest request, InferenceAgentRecord agent)
    {
        if (!ValidText(request.MachineName, 128) || !ValidText(request.Version, 64) ||
            request.LogicalProcessors is < 1 or > 4096 || request.TotalMemoryMb is < 1 or > 16777216 ||
            request.AppliedRevision < 0 || request.AppliedRevision > agent.Revision ||
            !ValidPort(request.DemoPort) || !ValidError(request.LastError) ||
            request.Workers is null || request.Workers.Length > 12)
            return false;
        var allowed = agent.DesiredWorkers.Select(value => value.WorkerId)
            .Concat(agent.RetiredWorkers.Select(value => value.WorkerId)).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var ports = new HashSet<int>();
        foreach (var worker in request.Workers)
        {
            if (worker is null || !ValidWorkerId(worker.WorkerId) || !allowed.Contains(worker.WorkerId) ||
                !seen.Add(worker.WorkerId) || !WorkerStates.Contains(worker.State) ||
                worker.Pid is <= 0 || !ValidPort(worker.Port) || worker.Threads is < 1 or > 24 ||
                !InferenceModels.IsAllowed(worker.ModelId) ||
                !ValidError(worker.LastError) || (worker.Port != 0 && !ports.Add(worker.Port)))
                return false;
        }
        return true;
    }

    internal InferenceAgentSummary Summary(InferenceAgentRecord agent) => new(
        1, agent.AgentId, agent.DisplayName, agent.Revision, agent.MaxWorkers, agent.ThreadsPerWorker,
        agent.CreatedAt, agent.LastSeenAt, IsOnline(agent), agent.MachineName, agent.AgentVersion,
        agent.LogicalProcessors, agent.TotalMemoryMb, agent.DemoPort, agent.LastError,
        agent.DesiredWorkers, agent.ObservedWorkers);

    private bool IsOnline(InferenceAgentRecord agent) =>
        agent.LastSeenAt is { } seen && clock.GetUtcNow() - seen <= TimeSpan.FromSeconds(options.OfflineAfterSeconds);
    private bool ValidThreads(int threads) => threads >= 1 && threads <= options.MaxThreadsPerWorker;
    private static bool ValidPort(int port) => port == 0 || port is >= 1024 and <= 65535;
    private static bool ValidError(string? error) => error is null || error.Length <= 512 &&
        !error.Any(value => char.IsControl(value) && value is not '\r' and not '\n' and not '\t');
    private static bool ValidText(string? value, int max) => !string.IsNullOrWhiteSpace(value) &&
        value.Length <= max && !value.Any(char.IsControl);
    private static bool ValidAgentId(string? value) => value is { Length: >= 3 and <= 48 } &&
        value[0] is >= 'a' and <= 'z' && char.IsAsciiLetterOrDigit(value[^1]) &&
        value.All(ch => ch is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');
    private static bool ValidWorkerId(string? value) => value is { Length: 32 } && Guid.TryParseExact(value, "N", out _);
    private static InferenceWorkerDesired NewWorker(int threads, string modelId) => new(Guid.NewGuid().ToString("N"), threads, modelId);
    private static int ThreadBudget(InferenceAgentRecord agent, InferenceWorkerDesired[] desired,
        InferenceWorkerThreadReservation[]? reservations = null) => desired.Concat(agent.RetiredWorkers)
        .Select(value => new InferenceWorkerThreadReservation(value.WorkerId, value.Threads))
        .Concat(agent.ObservedWorkers.Select(value => new InferenceWorkerThreadReservation(value.WorkerId, value.Threads)))
        .Concat(reservations ?? agent.ThreadReservations).GroupBy(value => value.WorkerId)
        .Sum(group => group.Max(value => value.Threads));
    private static InferenceWorkerThreadReservation[] MergeReservations(
        IEnumerable<InferenceWorkerThreadReservation> first, IEnumerable<InferenceWorkerThreadReservation> second) =>
        first.Concat(second).GroupBy(value => value.WorkerId)
            .Select(group => new InferenceWorkerThreadReservation(group.Key, group.Max(value => value.Threads))).ToArray();
    private static string? Redact(string? value, string token) => value?.Replace(token, "[redacted]", StringComparison.Ordinal);
}
