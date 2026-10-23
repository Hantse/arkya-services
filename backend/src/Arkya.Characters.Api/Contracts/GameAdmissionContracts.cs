using System.Text.Json.Serialization;
using Arkya.ServiceDefaults.Characters;

namespace Arkya.Characters.Api.Contracts;

public sealed record CharacterEntryContextResponse(bool HasCharacters, string? LastRealmId,
    Guid? LastCharacterId, DateTimeOffset? LastConnectedAt);
public sealed record AdmissionRequest(string? RealmId, int ProtocolVersion, string? BuildId);
public sealed record AdmissionResponse(string Ticket, DateTimeOffset ExpiresAt, string Endpoint,
    int ProtocolVersion, string BuildId, string RealmId, string ServerId)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? AreaId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ContentId { get; init; }
}
public sealed record ConsumeAdmissionRequest(string? Ticket);
public sealed record LeaseRequest(Guid LeaseId, long AuthorityEpoch);
public sealed record GamePosition(float X, float Y, float Z);
public sealed record GameForward(float X, float Z);
public sealed record ActorStateEnvelope(int SchemaVersion, string Payload);
public sealed record SavedWorldState(long Revision, GamePosition Position, GameForward Forward)
{
    public ActorStateEnvelope? Simulation { get; init; }
    public CharacterPossessionsSnapshot? Possessions { get; init; }
    public string? IslandId { get; init; }
}
public sealed record SaveWorldStateRequest(Guid LeaseId, long AuthorityEpoch, long ExpectedRevision,
    GamePosition? Position, GameForward? Forward)
{
    public ActorStateEnvelope? Simulation { get; init; }
    public CharacterPossessionsSnapshot? Possessions { get; init; }
}
public sealed record ConsumedAdmissionResponse(Guid AccountId, CharacterResponse Character, string RealmId,
    string ServerId, Guid LeaseId, long AuthorityEpoch, DateTimeOffset LeaseExpiresAt,
    DateTimeOffset SessionExpiresAt, SavedWorldState? State)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? AreaId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ContentId { get; init; }
}
public sealed record LeaseHeartbeatResponse(DateTimeOffset LeaseExpiresAt);
public sealed record PrepareTransferRequest(Guid TransferId, Guid LeaseId, long AuthorityEpoch,
    string TargetServerId, string TopologyVersion, GamePosition Position, GameForward Forward, ActorStateEnvelope Simulation)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? TransitionId { get; init; }
    public CharacterPossessionsSnapshot? Possessions { get; init; }
}
public sealed record PreparedTransferResponse(Guid TransferId, string Ticket, DateTimeOffset ExpiresAt,
    string Endpoint, string RealmId, string ServerId, string TopologyVersion);
public sealed record ClaimTransferRequest(string Ticket);
public sealed record ClaimedTransferResponse(Guid TransferId, Guid ClaimId, Guid CharacterId, DateTimeOffset ExpiresAt,
    ConsumedAdmissionResponse Admission);
public sealed record SealTransferRequest(Guid LeaseId, long AuthorityEpoch, long ExpectedRevision,
    GamePosition Position, GameForward Forward, ActorStateEnvelope Simulation)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ActorStateEnvelope? ArrivalSimulation { get; init; }
    public CharacterPossessionsSnapshot? Possessions { get; init; }
}
public sealed record CommitTransferRequest(Guid ClaimId);
public sealed record TransferStatusResponse(Guid TransferId, string Status, string ServerId, long AuthorityEpoch,
    DateTimeOffset ExpiresAt, long StateRevision);
