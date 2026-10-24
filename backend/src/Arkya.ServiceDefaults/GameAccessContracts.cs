using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;

namespace Arkya.ServiceDefaults;

public static class GameProtocol
{
    public const int Version = 1;
    public const string BuildId = "arkya-web-beta-v1";
    public const int HandoffVersion = 2;
    public const string HandoffBuildId = "arkya-partition-handoff-v1";
    public const string CharacterStatsBuildId = "arkya-character-stats-v1";
    public const int MultiAreaVersion = 3;
    public const string MultiAreaBuildId = "arkya-multi-area-v1";
    public const int DivingVersion = 4;
    public const string DivingBuildId = "arkya-diving-v1";
    public const int TownNpcVersion = 5;
    public const string TownNpcBuildId = "arkya-town-npcs-v1";
    public const int CraftingVersion = 6;
    public const string CraftingBuildId = "arkya-crafting-v1";
    public const int CraftSchoolsVersion = 7;
    public const string CraftSchoolsBuildId = "arkya-craft-schools-v1";
    public const int NpcVitalsVersion = 8;
    public const string NpcVitalsBuildId = "arkya-npc-vitals-v1";
    public const int CraftProgressionVersion = 9;
    public const string CraftProgressionBuildId = "arkya-craft-progression-v2";
    public static bool IsMultiArea(int version, string? buildId) =>
        version == MultiAreaVersion && buildId == MultiAreaBuildId || version == DivingVersion && buildId == DivingBuildId ||
        version == TownNpcVersion && buildId == TownNpcBuildId || version == CraftingVersion && buildId == CraftingBuildId ||
        version == CraftSchoolsVersion && buildId == CraftSchoolsBuildId ||
        version == NpcVitalsVersion && buildId == NpcVitalsBuildId ||
        version == CraftProgressionVersion && buildId == CraftProgressionBuildId;
    public static bool IsHandoff(int version, string? buildId) => version == HandoffVersion &&
        buildId is HandoffBuildId or CharacterStatsBuildId || IsMultiArea(version, buildId);
    public static bool IsSupported(int version, string? buildId) => version == Version && buildId == BuildId || IsHandoff(version, buildId);
    public static bool ValidId(string? value) => value is not null && Regex.IsMatch(value, @"\A[a-z][a-z0-9-]{1,29}[a-z0-9]\z");
    public static bool ValidEndpoint(string? value, bool development = false) =>
        value is { Length: <= 512 } && !value.Any(char.IsWhiteSpace) && !value.Contains('\\') &&
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.UserInfo.Length == 0 &&
        uri.Query.Length == 0 && uri.Fragment.Length == 0 && uri.Host.Length > 0 &&
        // Unity Transport copies the escaped AbsolutePath into FixedString128Bytes (125 payload bytes).
        Encoding.UTF8.GetByteCount(uri.AbsolutePath) <= 125 &&
        (uri.Scheme == "wss" || development && uri.Scheme == "ws" && uri.Host is "localhost" or "127.0.0.1");
}

public sealed record GameRealmEntry(string RealmId, string DisplayName, bool Ready, string ServerId,
    string Endpoint, int ProtocolVersion, string BuildId)
{
    public string? TopologyVersion { get; init; }
    public GameRealmPartition[] Partitions { get; init; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public GameRealmArea[]? Areas { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public GameAreaTransition[]? AreaTransitions { get; init; }
}
public sealed record GameRealmArea(string AreaId, string ContentId);
public sealed record GameAreaPosition(float X, float Y, float Z);
public sealed record GameAreaForward(float X, float Z);
public sealed record GameAreaTransition(string Id, string SourceAreaId, string TargetAreaId,
    GamePartitionBounds SourceBounds, GameAreaPosition TargetPosition,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] GameAreaForward? TargetForward = null);
public sealed record GamePartitionBounds(double MinX, double MinZ, double MaxX, double MaxZ);
// IslandId is the legacy Area identifier; a partition belongs to one complete map.
public sealed record GameRealmPartition(string ServerId, string Endpoint, string IslandId, int PartitionIndex, GamePartitionBounds Bounds);
public sealed record GameRealmCatalog(GameRealmEntry[] Realms)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public RetiringGameServer[]? RetiringServers { get; init; }
}
public sealed record RetiringGameServer(string RealmId, string ServerId);

public static class RetiringGameServers
{
    public static bool IsValid(GameRealmCatalog catalog)
    {
        if (catalog.RetiringServers is null) return true;
        if (catalog.RetiringServers.Length > 25600 || catalog.Realms is null || catalog.Realms.Length > 100) return false;
        var realms = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var realm in catalog.Realms)
            if (!realm.Ready && !realms.TryAdd(realm.RealmId,
                realm.Partitions.Select(partition => partition.ServerId).Append(realm.ServerId).ToHashSet(StringComparer.Ordinal))) return false;
        var identities = new HashSet<(string, string)>();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var server in catalog.RetiringServers)
        {
            if (server is null || !GameProtocol.ValidId(server.RealmId) || !GameProtocol.ValidId(server.ServerId) ||
                !realms.TryGetValue(server.RealmId, out var current) || current.Contains(server.ServerId) ||
                !identities.Add((server.RealmId, server.ServerId))) return false;
            counts[server.RealmId] = counts.GetValueOrDefault(server.RealmId) + 1;
            if (counts[server.RealmId] > 256) return false;
        }
        return true;
    }
}

public sealed class BrowserClientOptions
{
    public const string Policy = "arkya-browser";
    public string[] AllowedOrigins { get; set; } = [];
    public bool IsValid(bool development) => AllowedOrigins is not null && AllowedOrigins.All(value =>
        value is not null && value == value.Trim() && !value.Contains('*') && !value.EndsWith('/') &&
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && value == uri.GetLeftPart(UriPartial.Authority) &&
        (uri.Scheme == "https" || development && uri.Scheme == "http" && uri.Host is "localhost" or "127.0.0.1"));
}
