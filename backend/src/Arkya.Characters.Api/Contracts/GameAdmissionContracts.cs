namespace Arkya.Characters.Api.Contracts;

public sealed record AdmissionRequest(string? RealmId, int ProtocolVersion, string? BuildId);
public sealed record AdmissionResponse(string Ticket, DateTimeOffset ExpiresAt, string Endpoint,
    int ProtocolVersion, string BuildId, string RealmId, string ServerId);
public sealed record ConsumeAdmissionRequest(string? Ticket);
public sealed record LeaseRequest(Guid LeaseId, long AuthorityEpoch);
public sealed record GamePosition(float X, float Y, float Z);
public sealed record GameForward(float X, float Z);
public sealed record SavedWorldState(long Revision, GamePosition Position, GameForward Forward);
public sealed record SaveWorldStateRequest(Guid LeaseId, long AuthorityEpoch, long ExpectedRevision,
    GamePosition? Position, GameForward? Forward);
public sealed record ConsumedAdmissionResponse(Guid AccountId, CharacterResponse Character, string RealmId,
    string ServerId, Guid LeaseId, long AuthorityEpoch, DateTimeOffset LeaseExpiresAt,
    DateTimeOffset SessionExpiresAt, SavedWorldState? State);
public sealed record LeaseHeartbeatResponse(DateTimeOffset LeaseExpiresAt);
