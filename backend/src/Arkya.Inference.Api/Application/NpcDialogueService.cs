using Arkya.Inference.Api.Domain;
using Arkya.Inference.Api.Infrastructure;
using Arkya.ServiceDefaults.Inference;
using Microsoft.Extensions.Options;

namespace Arkya.Inference.Api.Application;

public sealed class NpcDialogueOptions
{
    public bool Enabled { get; set; }
    public string ServiceKey { get; set; } = "";
    public int Capacity { get; set; } = 128;
    public int TimeoutSeconds { get; set; } = 45;
    public int RetentionSeconds { get; set; } = 120;

    public bool IsValid() => (!Enabled || ServiceKey is { Length: >= 32 and <= 512 } &&
        ServiceKey == ServiceKey.Trim() && !ServiceKey.Any(char.IsControl)) &&
        Capacity is >= 1 and <= 1024 && TimeoutSeconds is >= 5 and <= 60 &&
        RetentionSeconds >= TimeoutSeconds && RetentionSeconds <= 300;
}

// Dialogue is ephemeral presentation. A restart discards requests; dedicateds use their authored fallback.
// Run one Inference replica until a distributed queue replaces this bounded coordinator.
public sealed class NpcDialogueService(IInferenceRepository agents, IOptions<NpcDialogueOptions> settings,
    IOptions<InferenceOptions> inferenceSettings, TimeProvider clock)
{
    private sealed class Entry(NpcDialogueRequest request, DateTimeOffset now, NpcDialogueOptions options)
    {
        public NpcDialogueRequest Request { get; } = request;
        public DateTimeOffset ExpiresAt { get; } = now.AddSeconds(options.TimeoutSeconds);
        public DateTimeOffset RetainUntil { get; } = now.AddSeconds(options.RetentionSeconds);
        public string? AgentId { get; set; }
        public string? AgentTokenHash { get; set; }
        public string? WorkerId { get; set; }
        public string? LeaseToken { get; set; }
        public NpcDialogueResponse Response { get; set; } = new(request.RequestId, "pending", null, null);
        public NpcDialogueWork Work => new(Request.RequestId, LeaseToken!, ExpiresAt, Request);
    }

    private readonly NpcDialogueOptions options = settings.Value;
    private readonly object gate = new();
    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);

    public async Task<InferenceResult<NpcDialogueResponse>> SubmitAsync(NpcDialogueRequest request,
        CancellationToken cancellationToken)
    {
        if (!options.Enabled) return new(null, "dialogue_disabled");
        if (!ValidRequest(request)) return new(null, "invalid_dialogue");
        lock (gate)
        {
            Prune();
            if (entries.TryGetValue(request.RequestId, out var existing))
                return existing.Request == request ? new(existing.Response, null) : new(null, "request_conflict");
        }
        var available = (await agents.ListAsync(cancellationToken)).Any(agent => Online(agent) &&
            Version.TryParse(agent.AgentVersion, out var version) && version >= new Version(1, 2) &&
            agent.DesiredWorkers.Any(desired => agent.ObservedWorkers.Any(observed =>
                observed.WorkerId == desired.WorkerId && observed.State is "ready" or "busy")));
        if (!available) return new(null, "inference_unavailable");
        lock (gate)
        {
            Prune();
            if (entries.TryGetValue(request.RequestId, out var existing))
                return existing.Request == request ? new(existing.Response, null) : new(null, "request_conflict");
            if (entries.Count >= options.Capacity) return new(null, "dialogue_capacity");
            var entry = new Entry(request, clock.GetUtcNow(), options);
            entries.Add(request.RequestId, entry);
            return new(entry.Response, null);
        }
    }

    public InferenceResult<NpcDialogueResponse> Get(string requestId)
    {
        if (!options.Enabled) return new(null, "dialogue_disabled");
        if (!ValidId(requestId)) return new(null, "invalid_dialogue");
        lock (gate)
        {
            Prune();
            return entries.TryGetValue(requestId, out var entry)
                ? new(entry.Response, null) : new(null, "dialogue_not_found");
        }
    }

    public async Task<InferenceResult<ClaimNpcDialogueResponse>> ClaimAsync(ClaimNpcDialogueRequest request,
        string? token, CancellationToken cancellationToken)
    {
        var agent = await AuthenticateAsync(request.AgentId, token, cancellationToken);
        if (agent is null) return new(null, "unauthorized_agent");
        if (!options.Enabled) return new(new(null), null);
        if (!ValidId(request.WorkerId) || !Online(agent) ||
            !agent.DesiredWorkers.Any(worker => worker.WorkerId == request.WorkerId) ||
            !agent.ObservedWorkers.Any(worker => worker.WorkerId == request.WorkerId && worker.State is "ready" or "busy"))
            return new(null, "worker_unavailable");
        lock (gate)
        {
            Prune();
            // A lost claim response can be retried by the same worker without assigning a second dialogue.
            var entry = entries.Values.FirstOrDefault(value => value.Response.Status == "pending" &&
                value.AgentId == request.AgentId && value.AgentTokenHash == agent.TokenHash && value.WorkerId == request.WorkerId);
            entry ??= entries.Values.FirstOrDefault(value => value.Response.Status == "pending" && value.LeaseToken is null);
            if (entry is null) return new(new(null), null);
            entry.AgentId = request.AgentId;
            entry.AgentTokenHash = agent.TokenHash;
            entry.WorkerId = request.WorkerId;
            entry.LeaseToken ??= AgentCredentials.Create();
            return new(new(entry.Work), null);
        }
    }

    public async Task<InferenceResult<NpcDialogueResponse>> CompleteAsync(CompleteNpcDialogueRequest request,
        string? token, CancellationToken cancellationToken)
    {
        var agent = await AuthenticateAsync(request.AgentId, token, cancellationToken);
        if (agent is null) return new(null, "unauthorized_agent");
        if (!options.Enabled) return new(null, "dialogue_disabled");
        if (!ValidId(request.RequestId) || !ValidId(request.WorkerId) ||
            !AgentCredentials.IsValidToken(request.LeaseToken) ||
            !(request.Error == "inference_failed" && request.Reply is null || request.Error is null && Text(request.Reply, 512)))
            return new(null, "invalid_dialogue");
        lock (gate)
        {
            Prune();
            if (!entries.TryGetValue(request.RequestId, out var entry)) return new(null, "dialogue_not_found");
            if (entry.AgentId != request.AgentId || entry.AgentTokenHash != agent.TokenHash ||
                entry.WorkerId != request.WorkerId || entry.LeaseToken != request.LeaseToken)
                return new(null, "dialogue_lease_mismatch");
            if (entry.Response.Status != "pending") return new(entry.Response, null);
            entry.Response = new(request.RequestId, request.Error is null ? "completed" : "failed",
                request.Reply?.Trim(), request.Error);
            return new(entry.Response, null);
        }
    }

    private async Task<InferenceAgentRecord?> AuthenticateAsync(string? agentId, string? token,
        CancellationToken cancellationToken)
    {
        if (agentId is not { Length: >= 3 and <= 48 } || !AgentCredentials.IsValidToken(token)) return null;
        var agent = await agents.FindAsync(agentId, cancellationToken);
        return agent is not null && AgentCredentials.Matches(token, agent.TokenHash) ? agent : null;
    }

    private bool Online(InferenceAgentRecord agent) => agent.LastSeenAt is { } seen &&
        clock.GetUtcNow() - seen <= TimeSpan.FromSeconds(inferenceSettings.Value.OfflineAfterSeconds);

    private void Prune()
    {
        var now = clock.GetUtcNow();
        foreach (var entry in entries.Values.ToArray())
        {
            if (entry.RetainUntil <= now) entries.Remove(entry.Request.RequestId);
            else if (entry.ExpiresAt <= now && entry.Response.Status == "pending")
                entry.Response = new(entry.Request.RequestId, "failed", null, "inference_timeout");
        }
    }

    private static bool ValidRequest(NpcDialogueRequest request) => ValidId(request.RequestId) &&
        request.NpcId is { Length: >= 1 and <= 64 } && request.NpcId.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_') &&
        Text(request.DisplayName, 80) && Text(request.Role, 120) && Text(request.Personality, 800) &&
        Text(request.PlayerMessage, 256) && Text(request.QuestContext, 1000, allowEmpty: true);

    private static bool ValidId(string? value) => value is { Length: 32 } && Guid.TryParseExact(value, "N", out _);
    private static bool Text(string? value, int maximum, bool allowEmpty = false) => value is not null &&
        value.Length <= maximum && (allowEmpty || !string.IsNullOrWhiteSpace(value)) && !value.Any(char.IsControl);
}
