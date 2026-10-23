using System.Security.Cryptography;
using System.Text;
using Arkya.Realms.Api.Domain;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Options;

namespace Arkya.Realms.Api.Application;

public sealed class GameEntryOptions
{
    public string ServiceKey { get; set; } = "";
    public StandaloneGameEntry[] StandaloneEntries { get; set; } = [];
    public bool IsValid(bool development) => StandaloneEntries is not null && StandaloneEntries.Length <= 100 &&
        (ServiceKey.Length is >= 32 and <= 512 || ServiceKey.Length == 0 && StandaloneEntries.Length == 0) &&
        StandaloneEntries.All(value => value is not null && value.IsValid(development)) &&
        StandaloneEntries.Select(value => value.RealmId).Distinct().Count() == StandaloneEntries.Length;
}

public sealed class StandaloneGameEntry
{
    public string RealmId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string ServerId { get; set; } = "";
    public string Endpoint { get; set; } = "";
    public bool Enabled { get; set; }
    public int ProtocolVersion { get; set; } = GameProtocol.Version;
    public string BuildId { get; set; } = GameProtocol.BuildId;
    public string? TopologyVersion { get; set; }
    public GameRealmPartition[] Partitions { get; set; } = [];
    public GameRealmArea[]? Areas { get; set; }
    public GameAreaTransition[]? AreaTransitions { get; set; }
    public bool IsValid(bool development) => GameProtocol.ValidId(RealmId) && GameProtocol.ValidId(ServerId) &&
        DeploymentOptions.ValidName(DisplayName) && GameProtocol.ValidEndpoint(Endpoint, development) &&
        GameProtocol.IsSupported(ProtocolVersion, BuildId) &&
        GameTopology.IsValid(new(RealmId, DisplayName, Enabled, ServerId, Endpoint, ProtocolVersion, BuildId)
            { TopologyVersion = TopologyVersion, Partitions = Partitions, Areas = Areas, AreaTransitions = AreaTransitions }, development);
}

public sealed class GameEntryService(IRealmRepository realms, IOptions<GameEntryOptions> configured)
{
    public bool Authorize(string? key) => configured.Value.ServiceKey.Length >= 32 && key is { Length: >= 32 and <= 512 } &&
        CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(key)),
            SHA256.HashData(Encoding.UTF8.GetBytes(configured.Value.ServiceKey)));

    public async Task<GameRealmCatalog> ListAsync(CancellationToken cancellationToken)
    {
        var entries = configured.Value.StandaloneEntries.Select(value => new GameRealmEntry(value.RealmId,
            value.DisplayName, value.Enabled, value.ServerId, value.Endpoint, value.ProtocolVersion, value.BuildId)
            { TopologyVersion = value.TopologyVersion, Partitions = value.Partitions, Areas = value.Areas, AreaTransitions = value.AreaTransitions }).ToList();
        foreach (var realm in await realms.ListAsync(cancellationToken))
        {
            // A deployed realm must never be silently overridden by standalone configuration.
            entries.RemoveAll(value => value.RealmId == realm.RealmId);
            if (RealmStatus.IsDeleting(realm.Status)) continue;
            if (realm.Profile.Topology is not null)
            {
                if (!RealmTopologyBuilder.SnapshotSupportsRuntime(realm.Profile)) continue;
                var islandServers = realm.Profile.Servers.Where(server => server.Kind == DedicatedKind.Island).ToArray();
                if (islandServers.Length == 0 || islandServers.Any(server => server.Transport != "WebSocket" || server.Bounds is null ||
                    server.IslandId is null || server.PartitionIndex is null || !GameProtocol.ValidEndpoint(server.PublicEndpoint) ||
                    !GameProtocol.IsHandoff(server.ProtocolVersion, server.BuildId))) continue;
                var initial = islandServers.OrderBy(server => server.IslandId == "starter-island" ? 0 : 1)
                    .ThenBy(server => server.IslandId, StringComparer.Ordinal).ThenBy(server => server.PartitionIndex).First();
                var entry = new GameRealmEntry(realm.RealmId, realm.DisplayName, realm.Status == RealmStatus.Ready,
                    initial.Id, initial.PublicEndpoint!, initial.ProtocolVersion, initial.BuildId)
                {
                    TopologyVersion = realm.DeploymentIdentity,
                    Areas = realm.Profile.Topology.Islands.Any(area => area.ContentId is not null) ? realm.Profile.Topology.Islands
                        .Select(area => new GameRealmArea(area.IslandId, area.ContentId ?? area.IslandId)).ToArray() : null,
                    AreaTransitions = realm.Profile.Topology.AreaTransitions,
                    Partitions = islandServers.Select(server => new GameRealmPartition(server.Id, server.PublicEndpoint!, server.IslandId!,
                        server.PartitionIndex!.Value, new(server.Bounds!.MinX, server.Bounds.MinZ, server.Bounds.MaxX, server.Bounds.MaxZ))).ToArray()
                };
                if (GameTopology.IsValid(entry)) entries.Add(entry);
                continue;
            }
            var destinations = realm.Profile.Servers.Where(value => value.Transport == "WebSocket" &&
                GameProtocol.ValidEndpoint(value.PublicEndpoint) && GameProtocol.IsSupported(value.ProtocolVersion, value.BuildId)).ToArray();
            var server = destinations.Length == 1 ? destinations[0] : null;
            if (server is not null) entries.Add(new(realm.RealmId, realm.DisplayName, realm.Status == RealmStatus.Ready,
                server.Id, server.PublicEndpoint!, server.ProtocolVersion, server.BuildId));
        }
        return new(entries.OrderBy(value => value.RealmId).ToArray());
    }
}
