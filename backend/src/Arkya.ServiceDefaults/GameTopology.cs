namespace Arkya.ServiceDefaults;

public static class GameTopology
{
    public static bool IsValid(GameRealmEntry entry, bool development = false) => entry.Partitions is not null &&
        (entry.Partitions.Length == 0 ? entry.TopologyVersion is null && entry.Areas is null && entry.AreaTransitions is null :
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
            Math.Max(left.Bounds.MinZ, right.Bounds.MinZ) < Math.Min(left.Bounds.MaxZ, right.Bounds.MaxZ))) && ValidMetadata(entry));

    public static string? ContentId(GameRealmEntry entry, string? areaId) => areaId is null ? null :
        entry.Areas?.SingleOrDefault(area => area.AreaId == areaId)?.ContentId ?? areaId;

    public static bool Contains(GamePartitionBounds bounds, double x, double z) =>
        x >= bounds.MinX && x <= bounds.MaxX && z >= bounds.MinZ && z <= bounds.MaxZ;

    public static bool ValidBounds(GamePartitionBounds? bounds) => bounds is not null &&
        double.IsFinite(bounds.MinX) && double.IsFinite(bounds.MinZ) && double.IsFinite(bounds.MaxX) && double.IsFinite(bounds.MaxZ) &&
        bounds.MaxX > bounds.MinX && bounds.MaxZ > bounds.MinZ;

    public static bool ValidPosition(GameAreaPosition? position) => position is not null &&
        float.IsFinite(position.X) && float.IsFinite(position.Y) && float.IsFinite(position.Z) &&
        Math.Abs(position.X) <= 100_000 && Math.Abs(position.Y) <= 100_000 && Math.Abs(position.Z) <= 100_000;

    public static bool ValidForward(GameAreaForward? forward) => forward is null ||
        float.IsFinite(forward.X) && float.IsFinite(forward.Z) &&
        forward.X * forward.X + forward.Z * forward.Z is >= .0001f and <= 1.01f;

    private static bool ValidMetadata(GameRealmEntry entry)
    {
        if ((entry.Areas is not null || entry.AreaTransitions is { Length: > 0 }) && !GameProtocol.IsMultiArea(entry.ProtocolVersion, entry.BuildId))
            return false;
        var areaIds = entry.Partitions.Select(partition => partition.IslandId).Distinct(StringComparer.Ordinal).ToArray();
        if (entry.Areas is { } areas && (areas.Length != areaIds.Length || areas.Length > 32 ||
            areas.Any(area => area is null || !GameProtocol.ValidId(area.AreaId) || !GameProtocol.ValidId(area.ContentId)) ||
            areas.Select(area => area.AreaId).Distinct(StringComparer.Ordinal).Count() != areas.Length ||
            areas.Any(area => !areaIds.Contains(area.AreaId, StringComparer.Ordinal)))) return false;
        if (entry.AreaTransitions is not { } transitions) return true;
        return transitions.Length <= 64 && transitions.All(transition => transition is not null &&
            GameProtocol.ValidId(transition.Id) && transition.SourceAreaId != transition.TargetAreaId &&
            areaIds.Contains(transition.SourceAreaId, StringComparer.Ordinal) && areaIds.Contains(transition.TargetAreaId, StringComparer.Ordinal) &&
            ValidBounds(transition.SourceBounds) && ValidPosition(transition.TargetPosition) && ValidForward(transition.TargetForward) &&
            TriggerInsideArea(entry, transition) &&
            Resolve(entry, transition.TargetAreaId, transition.TargetPosition.X, transition.TargetPosition.Z) is not null) &&
            transitions.Select(transition => transition.Id).Distinct(StringComparer.Ordinal).Count() == transitions.Length;
    }

    private static bool TriggerInsideArea(GameRealmEntry entry, GameAreaTransition transition)
    {
        var source = entry.Partitions.Where(partition => partition.IslandId == transition.SourceAreaId).ToArray();
        return transition.SourceBounds.MinX >= source.Min(partition => partition.Bounds.MinX) &&
            transition.SourceBounds.MinZ >= source.Min(partition => partition.Bounds.MinZ) &&
            transition.SourceBounds.MaxX <= source.Max(partition => partition.Bounds.MaxX) &&
            transition.SourceBounds.MaxZ <= source.Max(partition => partition.Bounds.MaxZ);
    }

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
