using Arkya.Realms.Api.Domain;

namespace Arkya.Realms.Api.Application;

public static class IslandPartitioning
{
    public static bool IsValid(RealmIslandTopology island) =>
        island.DedicatedCount is >= 1 and <= 64 &&
        double.IsFinite(island.Width) && island.Width is >= 1 and <= 1_000_000 &&
        double.IsFinite(island.Depth) && island.Depth is >= 1 and <= 1_000_000 &&
        double.IsFinite(island.OriginX) && Math.Abs(island.OriginX) <= 10_000_000 &&
        double.IsFinite(island.OriginZ) && Math.Abs(island.OriginZ) <= 10_000_000;

    public static AreaBounds[] Create(RealmIslandTopology island)
    {
        if (!IsValid(island)) throw new ArgumentOutOfRangeException(nameof(island));
        var columns = 1;
        var bestScore = double.PositiveInfinity;
        for (var candidate = 1; candidate <= island.DedicatedCount; candidate++)
        {
            if (island.DedicatedCount % candidate != 0) continue;
            var candidateRows = island.DedicatedCount / candidate;
            var score = Math.Abs(Math.Log((island.Width / candidate) / (island.Depth / candidateRows)));
            if (score < bestScore - 1e-12 || Math.Abs(score - bestScore) <= 1e-12 && candidate > columns)
            {
                columns = candidate;
                bestScore = score;
            }
        }
        var rows = island.DedicatedCount / columns;
        return Enumerable.Range(0, island.DedicatedCount).Select(index =>
        {
            var column = index % columns;
            var row = index / columns;
            return new AreaBounds(island.OriginX + island.Width * ((double)column / columns),
                island.OriginZ + island.Depth * ((double)row / rows),
                island.OriginX + island.Width * ((double)(column + 1) / columns),
                island.OriginZ + island.Depth * ((double)(row + 1) / rows));
        }).ToArray();
    }

    public static int? FindPartition(RealmIslandTopology island, double x, double z)
    {
        if (!double.IsFinite(x) || !double.IsFinite(z)) return null;
        var partitions = Create(island);
        var maxX = island.OriginX + island.Width;
        var maxZ = island.OriginZ + island.Depth;
        for (var index = 0; index < partitions.Length; index++)
        {
            var bounds = partitions[index];
            // Internal edges belong to the next cell; the island's outer maximum remains reachable.
            if (x >= bounds.MinX && (x < bounds.MaxX || bounds.MaxX == maxX && x == maxX) &&
                z >= bounds.MinZ && (z < bounds.MaxZ || bounds.MaxZ == maxZ && z == maxZ)) return index;
        }
        return null;
    }

    public static bool AreNeighbors(AreaBounds left, AreaBounds right) =>
        ((left.MaxX == right.MinX || right.MaxX == left.MinX) && Math.Max(left.MinZ, right.MinZ) < Math.Min(left.MaxZ, right.MaxZ)) ||
        ((left.MaxZ == right.MinZ || right.MaxZ == left.MinZ) && Math.Max(left.MinX, right.MinX) < Math.Min(left.MaxX, right.MaxX));
}
