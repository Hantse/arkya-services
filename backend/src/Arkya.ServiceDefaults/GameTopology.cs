namespace Arkya.ServiceDefaults;

public static class GameTopology
{
    public static bool IsValid(GameRealmEntry entry, bool development = false) => entry.Partitions is not null &&
        (entry.Partitions.Length == 0 ? entry.TopologyVersion is null :
        entry.TopologyVersion is { Length: >= 1 and <= 128 } && !entry.TopologyVersion.Any(char.IsControl) &&
        GameProtocol.IsHandoff(entry.ProtocolVersion, entry.BuildId) &&
        entry.Partitions.Length <= 256 && entry.Partitions.All(partition => partition is not null &&
            GameProtocol.ValidId(partition.ServerId) && GameProtocol.ValidId(partition.IslandId) &&
            GameProtocol.ValidEndpoint(partition.Endpoint, development) && partition.PartitionIndex >= 0 &&
            partition.Bounds is { } bounds && double.IsFinite(bounds.MinX) && double.IsFinite(bounds.MinZ) &&
            double.IsFinite(bounds.MaxX) && double.IsFinite(bounds.MaxZ) && bounds.MaxX > bounds.MinX && bounds.MaxZ > bounds.MinZ) &&
        entry.Partitions.Select(partition => partition.ServerId).Distinct(StringComparer.Ordinal).Count() == entry.Partitions.Length &&
        entry.Partitions.Select(partition => new Uri(partition.Endpoint).AbsoluteUri).Distinct(StringComparer.Ordinal).Count() == entry.Partitions.Length &&
        entry.Partitions.GroupBy(partition => partition.IslandId).All(island => island.Select(partition => partition.PartitionIndex).Distinct().Count() == island.Count()) &&
        entry.Partitions.Any(partition => partition.ServerId == entry.ServerId && partition.Endpoint == entry.Endpoint) &&
        !entry.Partitions.Any(left => entry.Partitions.Any(right => left.ServerId != right.ServerId && left.IslandId == right.IslandId &&
            Math.Max(left.Bounds.MinX, right.Bounds.MinX) < Math.Min(left.Bounds.MaxX, right.Bounds.MaxX) &&
            Math.Max(left.Bounds.MinZ, right.Bounds.MinZ) < Math.Min(left.Bounds.MaxZ, right.Bounds.MaxZ))));

    public static GameRealmPartition? Resolve(GameRealmEntry entry, string? islandId, double x, double z)
    {
        if (!double.IsFinite(x) || !double.IsFinite(z)) return null;
        islandId ??= entry.Partitions.FirstOrDefault(partition => partition.ServerId == entry.ServerId)?.IslandId;
        var island = entry.Partitions.Where(partition => partition.IslandId == islandId).ToArray();
        if (island.Length == 0) return null;
        var maxX = island.Max(partition => partition.Bounds.MaxX);
        var maxZ = island.Max(partition => partition.Bounds.MaxZ);
        return island.SingleOrDefault(partition => x >= partition.Bounds.MinX && z >= partition.Bounds.MinZ &&
            (x < partition.Bounds.MaxX || x == maxX && partition.Bounds.MaxX == maxX) &&
            (z < partition.Bounds.MaxZ || z == maxZ && partition.Bounds.MaxZ == maxZ));
    }

    public static bool AreNeighbors(GameRealmPartition left, GameRealmPartition right) => left.IslandId == right.IslandId &&
        (((left.Bounds.MaxX == right.Bounds.MinX || right.Bounds.MaxX == left.Bounds.MinX) &&
            Math.Max(left.Bounds.MinZ, right.Bounds.MinZ) <= Math.Min(left.Bounds.MaxZ, right.Bounds.MaxZ)) ||
        ((left.Bounds.MaxZ == right.Bounds.MinZ || right.Bounds.MaxZ == left.Bounds.MinZ) &&
            Math.Max(left.Bounds.MinX, right.Bounds.MinX) <= Math.Min(left.Bounds.MaxX, right.Bounds.MaxX)));
}
