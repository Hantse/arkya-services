using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Arkya.Characters.Api.Contracts;
using Arkya.ServiceDefaults;

namespace Arkya.Characters.Api.Application;

public sealed record GameLeaseRecord(Guid CharacterId, Guid AccountId, string RealmId, string ServerId,
    long Version, string? TicketHash, DateTimeOffset TicketExpiresAt, Guid LeaseId, long AuthorityEpoch,
    DateTimeOffset LeaseExpiresAt, DateTimeOffset SessionExpiresAt, SavedWorldState? State, string? SessionReference = null);

public enum GameWriteCondition { None, PendingTicket, ActiveLease }

public interface IGameLeaseStore
{
    Task<GameLeaseRecord?> FindCharacterAsync(Guid characterId, CancellationToken cancellationToken);
    Task<GameLeaseRecord?> FindTicketAsync(string ticketHash, CancellationToken cancellationToken);
    Task<bool> TryWriteAsync(GameLeaseRecord record, long? expectedVersion, CancellationToken cancellationToken, GameWriteCondition condition = GameWriteCondition.None);
}

public sealed record GameResult<T>(T? Value = default, string? Error = null, int Status = 200);

public sealed class GameAdmissionService(ICharacterRepository characters, IGameLeaseStore leases,
    IGameRealmCatalog realms, DedicatedKeyValidator servers, TimeProvider clock, ISessionReferenceValidator sessions)
{
    public const int TicketSeconds = 60;
    public const int LeaseSeconds = 90;
    public static string HashTicket(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public async Task<GameRealmCatalog> CatalogAsync(CancellationToken cancellationToken)
    {
        var catalog = await realms.ListAsync(cancellationToken);
        return new(catalog.Realms.Select(value => value with { Ready = value.Ready &&
            value.ProtocolVersion == GameProtocol.Version && value.BuildId == GameProtocol.BuildId &&
            servers.IsConfigured(value.RealmId, value.ServerId) }).ToArray());
    }

    public async Task<GameResult<AdmissionResponse>> IssueAsync(Guid characterId, Guid accountId, DateTimeOffset sessionExpiresAt,
        string sessionReference, AdmissionRequest request, CancellationToken cancellationToken)
    {
        if (!SessionReference.IsValid(sessionReference)) return new(Error: "session_expired", Status: 401);
        if (!GameProtocol.ValidId(request.RealmId)) return new(Error: "invalid_request", Status: 400);
        if (request.ProtocolVersion != GameProtocol.Version || request.BuildId != GameProtocol.BuildId)
            return new(Error: "incompatible_client", Status: 400);
        var now = clock.GetUtcNow();
        if (sessionExpiresAt <= now) return new(Error: "session_expired", Status: 401);
        var character = await characters.FindOwnedAsync(characterId, accountId, cancellationToken);
        if (character is null) return new(Error: "character_not_found", Status: 404);
        if (character.RealmId is not null && character.RealmId != request.RealmId) return new(Error: "realm_mismatch", Status: 409);
        var entry = (await CatalogAsync(cancellationToken)).Realms.SingleOrDefault(value => value.RealmId == request.RealmId && value.Ready);
        if (entry is null) return new(Error: "realm_unavailable", Status: 503);
        if (!await characters.TryBindRealmAsync(characterId, accountId, entry.RealmId, cancellationToken))
            return new(Error: "realm_mismatch", Status: 409);
        var ticket = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var expires = Min(now.AddSeconds(TicketSeconds), sessionExpiresAt);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var current = await leases.FindCharacterAsync(characterId, cancellationToken);
            now = clock.GetUtcNow();
            if (expires <= now) return new(Error: "session_expired", Status: 401);
            if (current is not null && (current.RealmId != entry.RealmId || current.AccountId != accountId))
                return new(Error: "realm_mismatch", Status: 409);
            if (current is not null && current.LeaseId != Guid.Empty && current.LeaseExpiresAt > now)
                return new(Error: "character_in_use", Status: 409);
            var next = new GameLeaseRecord(characterId, accountId, entry.RealmId, entry.ServerId,
                (current?.Version ?? 0) + 1, HashTicket(ticket), expires, Guid.Empty, current?.AuthorityEpoch ?? 0,
                now, sessionExpiresAt, current?.State, sessionReference);
            if (await leases.TryWriteAsync(next, current?.Version, cancellationToken))
                return new(new(ticket, expires, entry.Endpoint, entry.ProtocolVersion, entry.BuildId, entry.RealmId, entry.ServerId));
        }
        return new(Error: "character_in_use", Status: 409);
    }

    public async Task<GameResult<ConsumedAdmissionResponse>> ConsumeAsync(DedicatedServerIdentity server,
        ConsumeAdmissionRequest request, CancellationToken cancellationToken)
    {
        if (request.Ticket is null || !Regex.IsMatch(request.Ticket, @"\A[A-Za-z0-9_-]{43}\z"))
            return new(Error: "admission_rejected", Status: 409);
        var current = await leases.FindTicketAsync(HashTicket(request.Ticket), cancellationToken);
        var now = clock.GetUtcNow();
        if (current is null || current.TicketExpiresAt <= now || current.SessionExpiresAt <= now ||
            current.RealmId != server.RealmId || current.ServerId != server.ServerId || current.LeaseId != Guid.Empty || current.AuthorityEpoch == long.MaxValue)
            return new(Error: "admission_rejected", Status: 409);
        var character = await characters.FindOwnedAsync(current.CharacterId, current.AccountId, cancellationToken);
        if (character is null || character.RealmId != current.RealmId) return new(Error: "admission_rejected", Status: 409);
        var validation = await ValidateSessionAsync(current, cancellationToken);
        if (validation.Status == SessionValidationStatus.Unavailable) return new(Error: "dependency_unavailable", Status: 503);
        if (validation.Status != SessionValidationStatus.Valid) return new(Error: "admission_rejected", Status: 409);
        now = clock.GetUtcNow();
        var sessionExpiry = Min(current.SessionExpiresAt, validation.Session!.ExpiresAt);
        if (current.TicketExpiresAt <= now || sessionExpiry <= now) return new(Error: "admission_rejected", Status: 409);
        var next = current with { Version = current.Version + 1, TicketHash = null, LeaseId = Guid.NewGuid(),
            AuthorityEpoch = current.AuthorityEpoch + 1, LeaseExpiresAt = Min(now.AddSeconds(LeaseSeconds), sessionExpiry), SessionExpiresAt = sessionExpiry };
        if (!await leases.TryWriteAsync(next, current.Version, cancellationToken, GameWriteCondition.PendingTicket)) return new(Error: "admission_rejected", Status: 409);
        return new(new(current.AccountId, CharacterResponse.From(character), current.RealmId, current.ServerId,
            next.LeaseId, next.AuthorityEpoch, next.LeaseExpiresAt, next.SessionExpiresAt, next.State));
    }

    public async Task<GameResult<LeaseHeartbeatResponse>> HeartbeatAsync(Guid characterId, DedicatedServerIdentity server,
        LeaseRequest request, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var current = await leases.FindCharacterAsync(characterId, cancellationToken);
            var now = clock.GetUtcNow();
            if (!Owns(current, server, request.LeaseId, request.AuthorityEpoch, now)) return new(Error: "lease_rejected", Status: 409);
            var validation = await ValidateSessionAsync(current!, cancellationToken);
            if (validation.Status == SessionValidationStatus.Unavailable) return new(Error: "dependency_unavailable", Status: 503);
            if (validation.Status != SessionValidationStatus.Valid) return new(Error: "lease_rejected", Status: 409);
            now = clock.GetUtcNow();
            var sessionExpiry = Min(current!.SessionExpiresAt, validation.Session!.ExpiresAt);
            if (!Owns(current, server, request.LeaseId, request.AuthorityEpoch, now) || sessionExpiry <= now) return new(Error: "lease_rejected", Status: 409);
            var next = current with { Version = current.Version + 1, LeaseExpiresAt = Min(now.AddSeconds(LeaseSeconds), sessionExpiry), SessionExpiresAt = sessionExpiry };
            if (await leases.TryWriteAsync(next, current.Version, cancellationToken, GameWriteCondition.ActiveLease)) return new(new(next.LeaseExpiresAt));
        }
        return new(Error: "lease_rejected", Status: 409);
    }

    public async Task<GameResult<SavedWorldState>> SaveAsync(Guid characterId, DedicatedServerIdentity server,
        SaveWorldStateRequest request, CancellationToken cancellationToken)
    {
        if (request.ExpectedRevision is < 0 or long.MaxValue || !Valid(request.Position, request.Forward))
            return new(Error: "invalid_request", Status: 400);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var current = await leases.FindCharacterAsync(characterId, cancellationToken);
            var now = clock.GetUtcNow();
            if (!Owns(current, server, request.LeaseId, request.AuthorityEpoch, now)) return new(Error: "lease_rejected", Status: 409);
            var validation = await ValidateSessionAsync(current!, cancellationToken);
            if (validation.Status == SessionValidationStatus.Unavailable) return new(Error: "dependency_unavailable", Status: 503);
            if (validation.Status != SessionValidationStatus.Valid) return new(Error: "lease_rejected", Status: 409);
            now = clock.GetUtcNow();
            var sessionExpiry = Min(current!.SessionExpiresAt, validation.Session!.ExpiresAt);
            if (!Owns(current, server, request.LeaseId, request.AuthorityEpoch, now) || sessionExpiry <= now) return new(Error: "lease_rejected", Status: 409);
            var state = current!.State;
            if (state is not null && state.Revision == request.ExpectedRevision + 1 && state.Position == request.Position && state.Forward == request.Forward)
                return new(state);
            if ((state?.Revision ?? 0) != request.ExpectedRevision) return new(Error: "state_conflict", Status: 409);
            var nextState = new SavedWorldState(request.ExpectedRevision + 1, request.Position!, request.Forward!);
            var next = current with { Version = current.Version + 1, State = nextState,
                SessionExpiresAt = sessionExpiry, LeaseExpiresAt = Min(current.LeaseExpiresAt, sessionExpiry) };
            if (await leases.TryWriteAsync(next, current.Version, cancellationToken, GameWriteCondition.ActiveLease)) return new(nextState);
        }
        return new(Error: "state_conflict", Status: 409);
    }

    public async Task<bool> ReleaseAsync(Guid characterId, DedicatedServerIdentity server, LeaseRequest request, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var current = await leases.FindCharacterAsync(characterId, cancellationToken);
            var now = clock.GetUtcNow();
            if (!Owns(current, server, request.LeaseId, request.AuthorityEpoch, now)) return false;
            var next = current! with { Version = current.Version + 1, LeaseId = Guid.Empty, LeaseExpiresAt = now };
            if (await leases.TryWriteAsync(next, current.Version, cancellationToken, GameWriteCondition.ActiveLease)) return true;
        }
        return false;
    }

    private async Task<SessionValidationResult> ValidateSessionAsync(GameLeaseRecord value, CancellationToken cancellationToken)
    {
        if (!SessionReference.IsValid(value.SessionReference)) return new(SessionValidationStatus.Invalid);
        var result = await sessions.ValidateReferenceAsync(value.SessionReference!, cancellationToken);
        if (result.Status != SessionValidationStatus.Valid) return result;
        return result.Session is { } session && session.AccountId == value.AccountId && session.ExpiresAt > clock.GetUtcNow()
            ? result : new(SessionValidationStatus.Invalid);
    }

    private static bool Owns(GameLeaseRecord? value, DedicatedServerIdentity server, Guid lease, long epoch, DateTimeOffset now) =>
        value is not null && lease != Guid.Empty && value.LeaseId == lease && epoch == value.AuthorityEpoch &&
        value.RealmId == server.RealmId && value.ServerId == server.ServerId && value.LeaseExpiresAt > now && value.SessionExpiresAt > now;
    private static DateTimeOffset Min(DateTimeOffset first, DateTimeOffset second) => first < second ? first : second;
    private static bool Valid(GamePosition? position, GameForward? forward) => position is not null && forward is not null &&
        float.IsFinite(position.X) && float.IsFinite(position.Y) && float.IsFinite(position.Z) &&
        Math.Abs(position.X) <= 100000 && Math.Abs(position.Y) <= 100000 && Math.Abs(position.Z) <= 100000 &&
        float.IsFinite(forward.X) && float.IsFinite(forward.Z) && forward.X * forward.X + forward.Z * forward.Z is >= .0001f and <= 1.01f;
}
