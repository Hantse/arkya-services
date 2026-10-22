using Arkya.Realms.Api.Domain;

namespace Arkya.Realms.Api.Contracts;

public sealed record CreateRealmRequest(string RealmId, string DisplayName, string TargetId, string ProfileId,
    RealmTopology? Topology = null);
public sealed record DeployRealmRequest(long ExpectedRevision);
public sealed record UpdateRealmTopologyRequest(long ExpectedRevision, string ProfileId, RealmTopology? Topology);
public sealed record RestartRealmRequest(long ExpectedRevision, string Reason);
public sealed record UpdateRealmRequest(long ExpectedRevision, string Reason, string ExpectedImage);
public sealed record RealmOperationsOptions(bool CanRestart, bool CanUpdate, string? UpdateImage,
    long? UpdateVersion, string? UnavailableReason);
public sealed record RealmServerSummary(string Id, int Port, int ReadyReplicas, int DesiredReplicas, string? Address,
    string Transport = "Udp", string? PublicEndpoint = null)
{
    public string Kind { get; init; } = DedicatedKind.Dedicated;
    public string? IslandId { get; init; }
    public int? PartitionIndex { get; init; }
    public int? PartitionCount { get; init; }
    public AreaBounds? Bounds { get; init; }
}
public sealed record RealmSummary(string RealmId, string DisplayName, string TargetId, string Namespace,
    string ProfileId, string Image, long Revision, string Status, string? LastError, DateTimeOffset UpdatedAt,
    RealmServerSummary[] Servers)
{
    public RealmOperation[] Operations { get; init; } = [];
    public RealmTopology? Topology { get; init; }
    public bool TopologyRuntimeSupported { get; init; }

    public static RealmSummary From(RealmRecord realm) => new(realm.RealmId, realm.DisplayName, realm.TargetId,
        realm.Namespace, realm.Profile.Id, realm.Profile.Image, realm.Revision, realm.Status, realm.LastError,
        realm.UpdatedAt, realm.Profile.Servers.Select(server =>
        {
            var observation = realm.Servers.FirstOrDefault(value => value.Id == server.Id);
            return new RealmServerSummary(server.Id, server.Port, observation?.ReadyReplicas ?? 0,
                realm.Status == RealmStatus.Draft ? 0 : 1, observation?.Address, server.Transport, server.PublicEndpoint)
            { Kind = server.Kind, IslandId = server.IslandId, PartitionIndex = server.PartitionIndex, PartitionCount = server.PartitionCount, Bounds = server.Bounds };
        }).ToArray()) { Operations = realm.Operations, Topology = realm.Profile.Topology, TopologyRuntimeSupported = realm.Profile.TopologyRuntimeSupported };
}
public sealed record TargetSummary(string Id, string DisplayName, string Namespace, string? RealmId = null);
public sealed record ProfileServerSummary(string Id, int Port, string Transport = "Udp", string? PublicEndpoint = null);
public sealed record ProfileSummary(string Id, string DisplayName, string Image, ProfileServerSummary[] Servers)
{
    public bool TopologySupported { get; init; }
}
public sealed record DeploymentOptionsResponse(TargetSummary[] Targets, ProfileSummary[] Profiles, bool DeploymentEnabled);
public sealed record ErrorResponse(string Code, string Message);
