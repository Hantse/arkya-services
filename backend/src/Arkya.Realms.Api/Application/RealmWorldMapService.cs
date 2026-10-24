using Arkya.Realms.Api.Domain;
using Arkya.ServiceDefaults;

namespace Arkya.Realms.Api.Application;

public sealed record RealmWorldMapSnapshot(string RealmId, string ServerId, long RealmRevision, string InstanceId,
    DateTimeOffset UpdatedAt, bool Truncated, int TotalEntities, RealmWorldMapEntity[] Entities);

public interface IRealmWorldMapRepository
{
    Task<RealmWorldMapSnapshot[]> FindAsync(string realmId, string[] serverIds, CancellationToken ct);
    Task ReplaceAsync(RealmWorldMapSnapshot snapshot, CancellationToken ct);
}

public sealed class RealmWorldMapService(IRealmRepository realms, IRealmWorldMapRepository repository, TimeProvider clock)
{
    public const int RefreshSeconds = 2;
    public const int StaleAfterSeconds = 10;
    public const int MaxEntities = 4096;
    public const long MaxRequestBodyBytes = 2 * 1024 * 1024;
    private const double CoordinateLimit = 11_000_000;
    private const double AreaMargin = 100;

    public async Task<RealmWorldMap?> GetAsync(string realmId, CancellationToken ct)
    {
        if (!DeploymentOptions.ValidId(realmId)) return null;
        var realm = await realms.FindAsync(realmId, ct);
        if (realm is null || realm.Status == RealmStatus.Deleted) return null;
        var now = clock.GetUtcNow();
        var snapshots = (await repository.FindAsync(realmId, realm.Profile.Servers.Select(server => server.Id).ToArray(), ct))
            .ToDictionary(snapshot => snapshot.ServerId, StringComparer.Ordinal);
        var servers = realm.Profile.Servers.Select(server =>
        {
            snapshots.TryGetValue(server.Id, out var snapshot);
            var status = realm.Status != RealmStatus.Ready ? "unavailable" : snapshot is null ? "unsupported" :
                snapshot.RealmRevision != realm.Revision || snapshot.UpdatedAt > now ||
                now - snapshot.UpdatedAt > TimeSpan.FromSeconds(StaleAfterSeconds) ? "stale" : "live";
            var live = status == "live";
            return new RealmWorldMapServer(server.Id, server.IslandId, snapshot?.UpdatedAt, status,
                live && snapshot!.Truncated, live ? snapshot!.TotalEntities : 0, live ? snapshot!.Entities : []);
        }).ToArray();
        return new(realmId, now, RefreshSeconds, StaleAfterSeconds, servers);
    }

    public async Task<string?> ReportAsync(string realmId, string serverId, RealmWorldMapReport request, CancellationToken ct)
    {
        if (!DeploymentOptions.ValidId(realmId) || !DeploymentOptions.ValidId(serverId) || !Valid(request))
            return "invalid_world_map";
        var realm = await realms.FindAsync(realmId, ct);
        if (realm is null || RealmStatus.IsDeleting(realm.Status)) return "realm_not_found";
        var server = realm.Profile.Servers.SingleOrDefault(value => value.Id == serverId);
        if (server is null) return "invalid_server";
        // A handoff can cross a partition before its authority changes; validate the complete Area instead.
        var area = realm.Profile.Topology?.Islands.SingleOrDefault(value => value.IslandId == server.IslandId);
        if (area is not null && request.Entities.Any(entity => entity.X < area.OriginX - AreaMargin ||
            entity.X > area.OriginX + area.Width + AreaMargin || entity.Z < area.OriginZ - AreaMargin ||
            entity.Z > area.OriginZ + area.Depth + AreaMargin)) return "invalid_world_map";
        await repository.ReplaceAsync(new(realmId, serverId, realm.Revision, request.InstanceId, clock.GetUtcNow(),
            request.Truncated, request.TotalEntities, request.Entities.ToArray()), ct);
        return null;
    }

    private static bool Valid(RealmWorldMapReport request)
    {
        if (request.Version != 1 || !Uuid(request.InstanceId) || request.Entities is not { Length: <= MaxEntities } ||
            request.TotalEntities < request.Entities.Length || request.Truncated != (request.TotalEntities > request.Entities.Length)) return false;
        var identities = new HashSet<(string Kind, string Id)>();
        foreach (var entity in request.Entities)
        {
            if (entity is null || !Printable(entity.Id) || !Printable(entity.Name) || entity.Kind is not ("player" or "npc" or "creature") ||
                !Coordinate(entity.X) || !Coordinate(entity.Y) || !Coordinate(entity.Z) ||
                (entity.Kind == "player" ? !Uuid(entity.Id) || entity.AuthorityEpoch <= 0 : entity.AuthorityEpoch != 0) ||
                !identities.Add((entity.Kind, entity.Id))) return false;
        }
        return true;
    }

    private static bool Printable(string? value) => value is { Length: > 0 and <= 128 } && value == value.Trim() && !value.Any(char.IsControl);
    private static bool Coordinate(double value) => double.IsFinite(value) && Math.Abs(value) <= CoordinateLimit;
    private static bool Uuid(string? value) => Guid.TryParseExact(value, "D", out var id) && id != Guid.Empty && value == id.ToString("D");
}
