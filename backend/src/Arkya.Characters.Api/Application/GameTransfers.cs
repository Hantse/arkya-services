using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Arkya.Characters.Api.Contracts;
using Arkya.ServiceDefaults;

namespace Arkya.Characters.Api.Application;

public static class TransferStatus
{
    public const string Prepared = "Prepared";
    public const string Claimed = "Claimed";
    public const string Sealed = "Sealed";
    public const string Committed = "Committed";
    public const string Aborted = "Aborted";
}

public sealed record GameTransferRecord(string TransferId, string SourceServerId, string SourceLeaseId, long SourceEpoch,
    string TargetServerId, string Endpoint, string TopologyVersion, string Status, string? ClaimId,
    DateTimeOffset ExpiresAt, SavedWorldState State, string? TargetLeaseId = null, long? TargetEpoch = null);

public sealed partial class GameAdmissionService
{
    public const int TransferSeconds = 45;

    public async Task<GameResult<GameRealmEntry>> TopologyAsync(DedicatedServerIdentity server, CancellationToken cancellationToken)
    {
        var entry = (await CatalogAsync(cancellationToken)).Realms.SingleOrDefault(value => value.RealmId == server.RealmId);
        return entry is not null && GameTopology.IsValid(entry, true) &&
            (entry.ServerId == server.ServerId || entry.Partitions.Any(partition => partition.ServerId == server.ServerId))
            ? new(entry) : new(Error: "topology_unavailable", Status: 503);
    }

    public async Task<GameResult<PreparedTransferResponse>> PrepareTransferAsync(Guid characterId, DedicatedServerIdentity server,
        PrepareTransferRequest request, CancellationToken cancellationToken)
    {
        if (request.TransferId == Guid.Empty || !GameProtocol.ValidId(request.TargetServerId) ||
            !Valid(request.Position, request.Forward) || !ValidSimulation(request.Simulation, true))
            return new(Error: "invalid_transfer", Status: 400);
        var catalog = await TopologyAsync(server, cancellationToken);
        if (catalog.Value is not { } entry) return new(Error: catalog.Error, Status: catalog.Status);
        if (!entry.Ready) return new(Error: "topology_unavailable", Status: 503);
        var source = entry.Partitions.SingleOrDefault(partition => partition.ServerId == server.ServerId);
        var target = entry.Partitions.SingleOrDefault(partition => partition.ServerId == request.TargetServerId);
        if (source is null || target is null || entry.TopologyVersion != request.TopologyVersion || !GameTopology.AreNeighbors(source, target))
            return new(Error: "invalid_transfer_destination", Status: 409);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var current = await leases.FindCharacterAsync(characterId, cancellationToken);
            var now = clock.GetUtcNow();
            if (current?.Transfer is { } previous && previous.TransferId == request.TransferId.ToString("D"))
            {
                if (current.RealmId != server.RealmId || previous.SourceServerId != server.ServerId || previous.SourceLeaseId != request.LeaseId.ToString("D") ||
                    previous.SourceEpoch != request.AuthorityEpoch || previous.TargetServerId != request.TargetServerId ||
                    previous.TopologyVersion != request.TopologyVersion || previous.Status == TransferStatus.Aborted || previous.ExpiresAt <= now)
                    return new(Error: "transfer_conflict", Status: 409);
                return new(Prepared(previous, TransferTicket(server, characterId, request.TransferId, request.LeaseId, request.AuthorityEpoch), current.RealmId));
            }
            if (!Owns(current, server, request.LeaseId, request.AuthorityEpoch, now)) return new(Error: "lease_rejected", Status: 409);
            if (current!.Transfer is { Status: TransferStatus.Prepared or TransferStatus.Claimed or TransferStatus.Sealed } active && active.ExpiresAt > now)
                return new(Error: "transfer_in_progress", Status: 409);
            var validation = await ValidateSessionAsync(current, cancellationToken);
            if (validation.Status == SessionValidationStatus.Unavailable) return new(Error: "dependency_unavailable", Status: 503);
            if (validation.Status != SessionValidationStatus.Valid) return new(Error: "lease_rejected", Status: 409);
            now = clock.GetUtcNow();
            if (!Owns(current, server, request.LeaseId, request.AuthorityEpoch, now)) return new(Error: "lease_rejected", Status: 409);
            var ticket = TransferTicket(server, characterId, request.TransferId, request.LeaseId, request.AuthorityEpoch);
            var expires = Min(now.AddSeconds(TransferSeconds), Min(current.LeaseExpiresAt, validation.Session!.ExpiresAt));
            var state = new SavedWorldState(current.State?.Revision ?? 0, request.Position, request.Forward)
                { Simulation = request.Simulation, IslandId = target.IslandId };
            var transfer = new GameTransferRecord(request.TransferId.ToString("D"), server.ServerId, request.LeaseId.ToString("D"),
                request.AuthorityEpoch, target.ServerId, target.Endpoint, request.TopologyVersion, TransferStatus.Prepared, null, expires, state);
            var next = current with { Version = current.Version + 1, TicketHash = HashTicket(ticket), TicketExpiresAt = expires, Transfer = transfer };
            if (await leases.TryWriteAsync(next, current.Version, cancellationToken, GameWriteCondition.ActiveLease))
                return new(Prepared(transfer, ticket, current.RealmId));
        }
        return new(Error: "transfer_conflict", Status: 409);
    }

    public async Task<GameResult<ClaimedTransferResponse>> ClaimTransferAsync(DedicatedServerIdentity server,
        ClaimTransferRequest request, CancellationToken cancellationToken)
    {
        if (request.Ticket is not { Length: 43 }) return new(Error: "transfer_rejected", Status: 409);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var current = await leases.FindTicketAsync(HashTicket(request.Ticket), cancellationToken);
            var now = clock.GetUtcNow();
            if (current?.Transfer is not { } transfer || current.RealmId != server.RealmId || transfer.TargetServerId != server.ServerId ||
                transfer.ExpiresAt <= now || current.SessionExpiresAt <= now || transfer.Status == TransferStatus.Aborted)
                return new(Error: "transfer_rejected", Status: 409);
            var validation = await ValidateSessionAsync(current, cancellationToken);
            if (validation.Status == SessionValidationStatus.Unavailable) return new(Error: "dependency_unavailable", Status: 503);
            if (validation.Status != SessionValidationStatus.Valid) return new(Error: "transfer_rejected", Status: 409);
            now = clock.GetUtcNow();
            if (transfer.ExpiresAt <= now || current.LeaseExpiresAt <= now || current.SessionExpiresAt <= now)
                return new(Error: "transfer_rejected", Status: 409);
            if (transfer.Status == TransferStatus.Committed && (current.LeaseId.ToString("D") != transfer.TargetLeaseId || current.AuthorityEpoch != transfer.TargetEpoch))
                return new(Error: "lease_rejected", Status: 409);
            if (transfer.Status == TransferStatus.Prepared)
            {
                transfer = transfer with { Status = TransferStatus.Claimed, ClaimId = Guid.NewGuid().ToString("D") };
                var next = current with { Version = current.Version + 1, Transfer = transfer };
                if (!await leases.TryWriteAsync(next, current.Version, cancellationToken, GameWriteCondition.TransferPending)) continue;
                current = next;
            }
            if (transfer.ClaimId is null || transfer.Status is not (TransferStatus.Claimed or TransferStatus.Sealed or TransferStatus.Committed))
                return new(Error: "transfer_rejected", Status: 409);
            var admission = await TransferAdmissionAsync(current, transfer.Status == TransferStatus.Committed, cancellationToken);
            return admission is null ? new(Error: "transfer_rejected", Status: 409) :
                new(new(Guid.Parse(transfer.TransferId), Guid.Parse(transfer.ClaimId), current.CharacterId, transfer.ExpiresAt, admission));
        }
        return new(Error: "transfer_conflict", Status: 409);
    }

    public async Task<GameResult<TransferStatusResponse>> SealTransferAsync(Guid characterId, Guid transferId,
        DedicatedServerIdentity server, SealTransferRequest request, CancellationToken cancellationToken)
    {
        if (request.ExpectedRevision is < 0 or long.MaxValue || !Valid(request.Position, request.Forward) || !ValidSimulation(request.Simulation, true))
            return new(Error: "invalid_transfer", Status: 400);
        var catalog = await TopologyAsync(server, cancellationToken);
        if (catalog.Value is not { } entry) return new(Error: catalog.Error, Status: catalog.Status);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var current = await leases.FindCharacterAsync(characterId, cancellationToken);
            var now = clock.GetUtcNow();
            if (!MatchesSource(current, transferId, server, request.LeaseId, request.AuthorityEpoch) || current!.Transfer is not { } transfer)
                return new(Error: "transfer_rejected", Status: 409);
            if (transfer.Status is TransferStatus.Sealed or TransferStatus.Committed)
                return SameState(transfer.State, request.Position, request.Forward, request.Simulation) && transfer.State.Revision == request.ExpectedRevision + 1
                    ? new(Status(current)) : new(Error: "transfer_conflict", Status: 409);
            if (transfer.Status != TransferStatus.Claimed || transfer.ExpiresAt <= now || !Owns(current, server, request.LeaseId, request.AuthorityEpoch, now))
                return new(Error: "transfer_rejected", Status: 409);
            if (entry.TopologyVersion != transfer.TopologyVersion ||
                GameTopology.Resolve(entry, transfer.State.IslandId, request.Position.X, request.Position.Z)?.ServerId != transfer.TargetServerId)
                return new(Error: "invalid_transfer_destination", Status: 409);
            if ((current.State?.Revision ?? 0) != request.ExpectedRevision) return new(Error: "state_conflict", Status: 409);
            var validation = await ValidateSessionAsync(current, cancellationToken);
            if (validation.Status == SessionValidationStatus.Unavailable) return new(Error: "dependency_unavailable", Status: 503);
            if (validation.Status != SessionValidationStatus.Valid) return new(Error: "lease_rejected", Status: 409);
            if (transfer.ExpiresAt <= clock.GetUtcNow() || !Owns(current, server, request.LeaseId, request.AuthorityEpoch, clock.GetUtcNow()))
                return new(Error: "transfer_rejected", Status: 409);
            var state = new SavedWorldState(request.ExpectedRevision + 1, request.Position, request.Forward)
                { Simulation = request.Simulation, IslandId = transfer.State.IslandId };
            var next = current with { Version = current.Version + 1, State = state, Transfer = transfer with { Status = TransferStatus.Sealed, State = state } };
            if (await leases.TryWriteAsync(next, current.Version, cancellationToken, GameWriteCondition.TransferPending)) return new(Status(next));
        }
        return new(Error: "transfer_conflict", Status: 409);
    }

    public async Task<GameResult<ConsumedAdmissionResponse>> CommitTransferAsync(Guid characterId, Guid transferId,
        DedicatedServerIdentity server, CommitTransferRequest request, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var current = await leases.FindCharacterAsync(characterId, cancellationToken);
            var now = clock.GetUtcNow();
            if (current?.Transfer is not { } transfer || transfer.TransferId != transferId.ToString("D") ||
                current.RealmId != server.RealmId || transfer.TargetServerId != server.ServerId || transfer.ClaimId != request.ClaimId.ToString("D") || request.ClaimId == Guid.Empty)
                return new(Error: "transfer_rejected", Status: 409);
            if (transfer.Status == TransferStatus.Committed)
            {
                if (current.LeaseId.ToString("D") != transfer.TargetLeaseId || current.AuthorityEpoch != transfer.TargetEpoch ||
                    current.ServerId != server.ServerId || current.LeaseExpiresAt <= now || current.SessionExpiresAt <= now)
                    return new(Error: "lease_rejected", Status: 409);
                var replayValidation = await ValidateSessionAsync(current, cancellationToken);
                if (replayValidation.Status == SessionValidationStatus.Unavailable) return new(Error: "dependency_unavailable", Status: 503);
                if (replayValidation.Status != SessionValidationStatus.Valid) return new(Error: "lease_rejected", Status: 409);
                var replay = await TransferAdmissionAsync(current, true, cancellationToken);
                return replay is null ? new(Error: "transfer_rejected", Status: 409) : new(replay);
            }
            if (transfer.Status != TransferStatus.Sealed || transfer.ExpiresAt <= now || current.AuthorityEpoch == long.MaxValue ||
                !Owns(current, new DedicatedServerIdentity { RealmId = current.RealmId, ServerId = transfer.SourceServerId },
                    Guid.Parse(transfer.SourceLeaseId), transfer.SourceEpoch, now)) return new(Error: "transfer_rejected", Status: 409);
            var catalog = await TopologyAsync(server, cancellationToken);
            if (catalog.Value is not { } entry || entry.TopologyVersion != transfer.TopologyVersion ||
                GameTopology.Resolve(entry, transfer.State.IslandId, transfer.State.Position.X, transfer.State.Position.Z)?.ServerId != server.ServerId)
                return new(Error: "topology_unavailable", Status: 503);
            var validation = await ValidateSessionAsync(current, cancellationToken);
            if (validation.Status == SessionValidationStatus.Unavailable) return new(Error: "dependency_unavailable", Status: 503);
            if (validation.Status != SessionValidationStatus.Valid) return new(Error: "lease_rejected", Status: 409);
            now = clock.GetUtcNow();
            if (transfer.ExpiresAt <= now || current.LeaseExpiresAt <= now || current.SessionExpiresAt <= now)
                return new(Error: "transfer_rejected", Status: 409);
            var sessionExpires = Min(current.SessionExpiresAt, validation.Session!.ExpiresAt);
            var leaseId = Guid.NewGuid();
            var epoch = current.AuthorityEpoch + 1;
            var next = current with { Version = current.Version + 1, ServerId = server.ServerId, LeaseId = leaseId,
                AuthorityEpoch = epoch, State = transfer.State, LeaseExpiresAt = Min(now.AddSeconds(LeaseSeconds), sessionExpires),
                SessionExpiresAt = sessionExpires, Transfer = transfer with { Status = TransferStatus.Committed, TargetLeaseId = leaseId.ToString("D"), TargetEpoch = epoch } };
            if (await leases.TryWriteAsync(next, current.Version, cancellationToken, GameWriteCondition.TransferPending))
            {
                var admission = await TransferAdmissionAsync(next, true, cancellationToken);
                return admission is null ? new(Error: "transfer_rejected", Status: 409) : new(admission);
            }
        }
        return new(Error: "transfer_conflict", Status: 409);
    }

    public async Task<GameResult<TransferStatusResponse>> TransferStatusAsync(Guid characterId, Guid transferId,
        DedicatedServerIdentity server, CancellationToken cancellationToken)
    {
        var current = await leases.FindCharacterAsync(characterId, cancellationToken);
        return current?.Transfer is { } transfer && transfer.TransferId == transferId.ToString("D") && current.RealmId == server.RealmId &&
            (transfer.SourceServerId == server.ServerId || transfer.TargetServerId == server.ServerId)
            ? new(Status(current)) : new(Error: "transfer_rejected", Status: 409);
    }

    public async Task<GameResult<TransferStatusResponse>> AbortTransferAsync(Guid characterId, Guid transferId,
        DedicatedServerIdentity server, LeaseRequest request, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var current = await leases.FindCharacterAsync(characterId, cancellationToken);
            if (!MatchesSource(current, transferId, server, request.LeaseId, request.AuthorityEpoch))
                return new(Error: "transfer_rejected", Status: 409);
            if (current!.Transfer!.Status is TransferStatus.Committed or TransferStatus.Aborted) return new(Status(current));
            if (!Owns(current, server, request.LeaseId, request.AuthorityEpoch, clock.GetUtcNow())) return new(Error: "lease_rejected", Status: 409);
            var next = current with { Version = current.Version + 1, TicketHash = null, Transfer = current.Transfer with { Status = TransferStatus.Aborted } };
            if (await leases.TryWriteAsync(next, current.Version, cancellationToken, GameWriteCondition.ActiveLease)) return new(Status(next));
        }
        return new(Error: "transfer_conflict", Status: 409);
    }

    private async Task<ConsumedAdmissionResponse?> TransferAdmissionAsync(GameLeaseRecord value, bool committed, CancellationToken cancellationToken)
    {
        var character = await characters.FindOwnedAsync(value.CharacterId, value.AccountId, cancellationToken);
        if (character is null || character.RealmId != value.RealmId || value.Transfer is not { } transfer) return null;
        return new(value.AccountId, CharacterResponse.From(character), value.RealmId, transfer.TargetServerId,
            committed ? value.LeaseId : Guid.Empty, committed ? value.AuthorityEpoch : transfer.SourceEpoch + 1,
            value.LeaseExpiresAt, value.SessionExpiresAt, committed ? value.State : transfer.State);
    }

    private static bool MatchesSource(GameLeaseRecord? current, Guid id, DedicatedServerIdentity server, Guid leaseId, long epoch) =>
        current?.Transfer is { } transfer && transfer.TransferId == id.ToString("D") && current.RealmId == server.RealmId &&
        transfer.SourceServerId == server.ServerId && transfer.SourceLeaseId == leaseId.ToString("D") && transfer.SourceEpoch == epoch;
    private static PreparedTransferResponse Prepared(GameTransferRecord transfer, string ticket, string realmId) =>
        new(Guid.Parse(transfer.TransferId), ticket, transfer.ExpiresAt, transfer.Endpoint, realmId, transfer.TargetServerId, transfer.TopologyVersion);
    private static TransferStatusResponse Status(GameLeaseRecord value) => new(Guid.Parse(value.Transfer!.TransferId), value.Transfer.Status,
        value.ServerId, value.AuthorityEpoch, value.Transfer.ExpiresAt, value.State?.Revision ?? 0);
    private static bool SameState(SavedWorldState state, GamePosition position, GameForward forward, ActorStateEnvelope simulation) =>
        state.Position == position && state.Forward == forward && state.Simulation == simulation;
    private static string TransferTicket(DedicatedServerIdentity server, Guid characterId, Guid transferId, Guid leaseId, long epoch) =>
        Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(server.Key),
            Encoding.UTF8.GetBytes(FormattableString.Invariant($"arkya-transfer-v1/{characterId:D}/{transferId:D}/{leaseId:D}/{epoch}")))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static bool ValidSimulation(ActorStateEnvelope? value, bool required)
    {
        if (value is null) return !required;
        if (value.SchemaVersion != 1 || value.Payload is not { Length: >= 2 and <= 32768 }) return false;
        try { using var parsed = JsonDocument.Parse(value.Payload, new JsonDocumentOptions { MaxDepth = 16 }); return parsed.RootElement.ValueKind == JsonValueKind.Object; }
        catch (JsonException) { return false; }
    }
}
