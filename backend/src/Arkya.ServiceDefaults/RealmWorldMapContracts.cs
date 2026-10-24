namespace Arkya.ServiceDefaults;

public sealed record RealmWorldMapEntity(string Id, string Kind, string Name, double X, double Y, double Z,
    long AuthorityEpoch = 0);

public sealed record RealmWorldMapReport(string InstanceId, int Version, RealmWorldMapEntity[] Entities,
    bool Truncated = false, int TotalEntities = 0);

public sealed record RealmWorldMapServer(string ServerId, string? IslandId, DateTimeOffset? UpdatedAt,
    string Status, bool Truncated, int TotalEntities, RealmWorldMapEntity[] Entities);

public sealed record RealmWorldMap(string RealmId, DateTimeOffset ObservedAt, int RefreshSeconds,
    int StaleAfterSeconds, RealmWorldMapServer[] Servers);
