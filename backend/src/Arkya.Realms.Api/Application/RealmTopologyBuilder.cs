using System.Globalization;
using Arkya.Realms.Api.Domain;
using Arkya.ServiceDefaults;

namespace Arkya.Realms.Api.Application;

public static class RealmTopologyBuilder
{
    public const int MaxIslands = 32;
    public const int MaxIslandIdLength = 20;
    public const int MaxDedicatedPerGroup = 64;
    public const int MaxTotalDedicated = 256;

    public static bool IsValid(RealmTopology? topology) => topology is not null &&
        topology.Islands is { Length: <= MaxIslands } &&
        topology.Islands.All(island => island is not null && DeploymentOptions.ValidId(island.IslandId) &&
            island.IslandId.Length <= MaxIslandIdLength && (island.ContentId is null || GameProtocol.ValidId(island.ContentId)) &&
            island.DedicatedCount is >= 1 and <= MaxDedicatedPerGroup && IslandPartitioning.IsValid(island)) &&
        topology.Islands.Select(island => island.IslandId).Distinct(StringComparer.Ordinal).Count() == topology.Islands.Length &&
        topology.InstanceDedicatedCount is >= 0 and <= MaxDedicatedPerGroup &&
        topology.RaidDedicatedCount is >= 0 and <= MaxDedicatedPerGroup &&
        topology.Islands.Sum(island => island.DedicatedCount) + topology.InstanceDedicatedCount + topology.RaidDedicatedCount
            is >= 1 and <= MaxTotalDedicated && ValidTransitions(topology);

    public static bool ValidTemplate(DeploymentProfile profile)
    {
        if (profile.TopologyServerIdPrefix is { } prefix &&
            !System.Text.RegularExpressions.Regex.IsMatch(prefix, @"\A[a-z](?:[a-z0-9-]{0,4}[a-z0-9])?\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant)) return false;
        if (profile.TopologyTemplateServerId is null) return profile.TopologyPublicEndpointTemplate is null && profile.TopologyServerIdPrefix is null;
        if (!DeploymentOptions.ValidId(profile.TopologyTemplateServerId)) return false;
        var template = profile.Servers.SingleOrDefault(server => server.Id == profile.TopologyTemplateServerId);
        return template is not null && (template.Transport == "Udp" ? profile.TopologyPublicEndpointTemplate is null :
            ValidEndpointTemplate(profile.TopologyPublicEndpointTemplate));
    }

    public static bool RuntimeSupported(DeploymentProfile profile) => profile.TopologyTemplateServerId is not null && ValidTemplate(profile) &&
        profile.Servers.Any(server => server.Id == profile.TopologyTemplateServerId && GameProtocol.IsHandoff(server.ProtocolVersion, server.BuildId));

    public static bool SnapshotSupportsRuntime(ProfileSnapshot snapshot) => snapshot.Topology is null ||
        snapshot.TopologyRuntimeSupported && snapshot.Topology.InstanceDedicatedCount == 0 && snapshot.Topology.RaidDedicatedCount == 0 &&
        snapshot.Servers.All(server => server.Kind == DedicatedKind.Island && GameProtocol.IsHandoff(server.ProtocolVersion, server.BuildId) &&
            (!RequiresMultiArea(snapshot.Topology) || GameProtocol.IsMultiArea(server.ProtocolVersion, server.BuildId)));

    private static bool RequiresMultiArea(RealmTopology topology) => topology.Islands.Any(area => area.ContentId is not null) ||
        topology.AreaTransitions is { Length: > 0 };

    public static ProfileSnapshot Snapshot(DeploymentProfile profile, string realmId, RealmTopology? topology)
    {
        if (topology is null) return profile.Snapshot();
        if (!IsValid(topology)) throw new ArgumentException("Invalid realm topology.", nameof(topology));
        var frozen = new RealmTopology(topology.Islands.OrderBy(island => island.IslandId, StringComparer.Ordinal)
            .Select(island => island with { }).ToArray(), topology.InstanceDedicatedCount, topology.RaidDedicatedCount)
        { AreaTransitions = topology.AreaTransitions is { Length: > 0 } transitions ? transitions.OrderBy(value => value.Id, StringComparer.Ordinal).ToArray() : null };
        var template = profile.Servers.SingleOrDefault(server => server.Id == profile.TopologyTemplateServerId) ?? profile.Servers[0];
        var servers = new List<ServerDefinition>();
        foreach (var island in frozen.Islands)
        {
            var bounds = IslandPartitioning.Create(island);
            for (var index = 0; index < island.DedicatedCount; index++)
                servers.Add(Server($"{profile.TopologyServerIdPrefix ?? "island"}-{island.IslandId}-{Slot(index)}", DedicatedKind.Island,
                    island.IslandId, index, island.DedicatedCount) with { Bounds = bounds[index] });
        }
        for (var index = 0; index < frozen.InstanceDedicatedCount; index++)
            servers.Add(Server($"instance-{Slot(index)}", DedicatedKind.Instance));
        for (var index = 0; index < frozen.RaidDedicatedCount; index++)
            servers.Add(Server($"raid-{Slot(index)}", DedicatedKind.Raid));
        var snapshot = profile.Snapshot() with { Servers = servers.ToArray(), Topology = frozen,
            TopologyRuntimeSupported = RuntimeSupported(profile) && frozen.InstanceDedicatedCount == 0 && frozen.RaidDedicatedCount == 0 &&
                (!RequiresMultiArea(frozen) || GameProtocol.IsMultiArea(template.ProtocolVersion, template.BuildId)) };
        if (snapshot.TopologyRuntimeSupported && !ValidGeneratedEndpoints(snapshot.Servers))
            throw new ArgumentException("Topology endpoints must be valid and distinct.", nameof(profile));
        return snapshot;

        ServerDefinition Server(string id, string kind, string? islandId = null, int? partitionIndex = null, int? partitionCount = null) =>
            new(id, template.Port, template.Args.ToArray())
            {
                Transport = template.Transport,
                PublicEndpoint = template.Transport == "WebSocket" && profile.TopologyPublicEndpointTemplate is { } endpoint ?
                    ExpandEndpoint(endpoint, realmId, id) : null,
                ProtocolVersion = template.ProtocolVersion, BuildId = template.BuildId,
                Kind = kind, IslandId = islandId, PartitionIndex = partitionIndex, PartitionCount = partitionCount
            };
    }

    private static string Slot(int index) => (index + 1).ToString("D2", CultureInfo.InvariantCulture);

    private static bool ValidTransitions(RealmTopology topology)
    {
        if (topology.AreaTransitions is not { Length: > 0 }) return true;
        var partitions = topology.Islands.SelectMany(area => IslandPartitioning.Create(area).Select((bounds, index) =>
            new GameRealmPartition($"island-{area.IslandId}-{Slot(index)}", $"wss://topology.invalid/{area.IslandId}/{index}", area.IslandId,
                index, new(bounds.MinX, bounds.MinZ, bounds.MaxX, bounds.MaxZ)))).ToArray();
        if (partitions.Length == 0) return false;
        return GameTopology.IsValid(new("validation-realm", "Validation", false, partitions[0].ServerId, partitions[0].Endpoint,
            GameProtocol.MultiAreaVersion, GameProtocol.MultiAreaBuildId) { Partitions = partitions, TopologyVersion = "validation", AreaTransitions = topology.AreaTransitions });
    }

    private static bool ValidEndpointTemplate(string? template)
    {
        if (template is not { Length: <= 512 } || !template.Contains("{serverId}", StringComparison.Ordinal)) return false;
        var longest = ExpandEndpoint(template, new string('r', 31), "island-" + new string('i', 20) + "-64");
        if (longest.Contains('{') || longest.Contains('}') || !GameProtocol.ValidEndpoint(longest)) return false;
        var first = new Uri(ExpandEndpoint(template, "sample-realm", "island-sample-01"));
        var second = new Uri(ExpandEndpoint(template, "sample-realm", "island-sample-02"));
        return first.AbsoluteUri != second.AbsoluteUri;
    }

    private static bool ValidGeneratedEndpoints(ServerDefinition[] servers)
    {
        var webSockets = servers.Where(server => server.Transport == "WebSocket").ToArray();
        return webSockets.All(server => GameProtocol.ValidEndpoint(server.PublicEndpoint)) &&
            webSockets.Select(server => new Uri(server.PublicEndpoint!).AbsoluteUri).Distinct(StringComparer.Ordinal).Count() == webSockets.Length;
    }

    private static string ExpandEndpoint(string template, string realmId, string serverId) =>
        template.Replace("{realmId}", realmId, StringComparison.Ordinal).Replace("{serverId}", serverId, StringComparison.Ordinal);
}
