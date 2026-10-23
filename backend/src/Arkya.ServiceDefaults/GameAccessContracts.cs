using System.Text;
using System.Text.RegularExpressions;

namespace Arkya.ServiceDefaults;

public static class GameProtocol
{
    public const int Version = 1;
    public const string BuildId = "arkya-web-beta-v1";
    public const int HandoffVersion = 2;
    public const string HandoffBuildId = "arkya-partition-handoff-v1";
    public const string CharacterStatsBuildId = "arkya-character-stats-v1";
    public static bool IsHandoff(int version, string? buildId) => version == HandoffVersion &&
        buildId is HandoffBuildId or CharacterStatsBuildId;
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
}
public sealed record GamePartitionBounds(double MinX, double MinZ, double MaxX, double MaxZ);
public sealed record GameRealmPartition(string ServerId, string Endpoint, string IslandId, int PartitionIndex, GamePartitionBounds Bounds);
public sealed record GameRealmCatalog(GameRealmEntry[] Realms);

public sealed class BrowserClientOptions
{
    public const string Policy = "arkya-browser";
    public string[] AllowedOrigins { get; set; } = [];
    public bool IsValid(bool development) => AllowedOrigins is not null && AllowedOrigins.All(value =>
        value is not null && value == value.Trim() && !value.Contains('*') && !value.EndsWith('/') &&
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && value == uri.GetLeftPart(UriPartial.Authority) &&
        (uri.Scheme == "https" || development && uri.Scheme == "http" && uri.Host is "localhost" or "127.0.0.1"));
}
