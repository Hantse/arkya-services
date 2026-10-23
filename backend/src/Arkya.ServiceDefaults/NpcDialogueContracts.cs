namespace Arkya.ServiceDefaults.Inference;

// Only the authoritative dedicated supplies the NPC profile and quest context.
public sealed record NpcDialogueRequest(string RequestId, string NpcId, string DisplayName,
    string Role, string Personality, string PlayerMessage, string QuestContext);

public sealed record NpcDialogueResponse(string RequestId, string Status, string? Reply, string? Error);

public sealed record ClaimNpcDialogueRequest(string AgentId, string WorkerId);

public sealed record NpcDialogueWork(string RequestId, string LeaseToken, DateTimeOffset ExpiresAt,
    NpcDialogueRequest Request);

public sealed record ClaimNpcDialogueResponse(NpcDialogueWork? Job);

public sealed record CompleteNpcDialogueRequest(string AgentId, string WorkerId, string RequestId,
    string LeaseToken, string? Reply, string? Error);
