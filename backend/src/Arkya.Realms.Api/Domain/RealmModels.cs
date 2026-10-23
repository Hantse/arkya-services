using System.Text.Json.Serialization;

namespace Arkya.Realms.Api.Domain;

public static class RealmStatus
{
    public const string Draft = "Draft";
    public const string Pending = "Pending";
    public const string Deploying = "Deploying";
    public const string Ready = "Ready";
    public const string Failed = "Failed";
    public const string Deleting = "Deleting";
    public const string DeleteFailed = "DeleteFailed";
    public const string Deleted = "Deleted";

    public static bool IsDeleting(string status) => status is Deleting or DeleteFailed or Deleted;
}

public static class DedicatedKind
{
    public const string Dedicated = "Dedicated";
    public const string Island = "Island";
    public const string Instance = "Instance";
    public const string Raid = "Raid";
}

public sealed record RealmIslandTopology(string IslandId, int DedicatedCount,
    double Width = 1000, double Depth = 1000, double OriginX = 0, double OriginZ = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? GridColumns = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? GridRows = null);
public sealed record AreaBounds(double MinX, double MinZ, double MaxX, double MaxZ);
public sealed record RealmTopology(RealmIslandTopology[] Islands, int InstanceDedicatedCount = 0, int RaidDedicatedCount = 0);

public sealed record ServerDefinition(string Id, int Port, string[] Args)
{
    public string Transport { get; init; } = "Udp";
    public string? PublicEndpoint { get; init; }
    public int ProtocolVersion { get; init; } = Arkya.ServiceDefaults.GameProtocol.Version;
    public string BuildId { get; init; } = Arkya.ServiceDefaults.GameProtocol.BuildId;
    public string Kind { get; init; } = DedicatedKind.Dedicated;
    public string? IslandId { get; init; }
    public int? PartitionIndex { get; init; }
    public int? PartitionCount { get; init; }
    public AreaBounds? Bounds { get; init; }
}
public sealed record ProfileSnapshot(string Id, string Image, ServerDefinition[] Servers,
    string? EnvironmentSecretName, string? ImagePullSecretName)
{
    public RealmTopology? Topology { get; init; }
    public bool TopologyRuntimeSupported { get; init; }
}
public sealed record ServerObservation(string Id, int ReadyReplicas, string? Address)
{
    public string? ReadyImage { get; init; }
    public long? ReadyRestartRevision { get; init; }
}

public sealed record RealmImageTransition(long Version, string PreviousImage, string Image, long PreviousVersion = 0);
public sealed record RealmRestartTransition(long Revision, long PreviousRevision)
{
    public bool Completed { get; init; }
}
public sealed record RealmOperation(string Kind, long Revision, long ExpectedRevision, string RequestedBy,
    string Reason, DateTimeOffset RequestedAt, DateTimeOffset? CompletedAt, string Status, string? Error,
    string PreviousImage, string Image);

public sealed record RealmRecord(string RealmId, string DisplayName, string TargetId, string Namespace,
    ProfileSnapshot Profile, string DeploymentIdentity, long Revision, long Version, long? RequestedFromRevision,
    string Status, string? LastError, DateTimeOffset UpdatedAt, DateTimeOffset? DeploymentStartedAt,
    ServerObservation[] Servers)
{
    public RealmImageTransition? ImageRelease { get; init; }
    public RealmRestartTransition? Restart { get; init; }
    public RealmOperation[] Operations { get; init; } = [];
    public RealmRuntimeConfiguration RuntimeConfiguration { get; init; } = new();
    public DateTimeOffset? LastObservedAt { get; init; }
    public DateTimeOffset? DeletedAt { get; init; }
}
