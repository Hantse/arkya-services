namespace Arkya.Admin.Realms;

public sealed record RealmIslandView(RealmIslandTopology Plan, RealmServer[] Servers, bool FixedProfile = false);

public static class RealmIslandPresentation
{
    // Local, reviewed images only: island identifiers never become file paths.
    public static string? ImagePath(string islandId) => islandId == "starter-island" ? "/images/islands/starter-island.png" : null;
    public static string Label(string islandId) => islandId == "starter-island" ? "Starter Island" : islandId;
    public static bool ImageMatchesPlan(RealmIslandTopology plan) => (plan.ContentId ?? plan.IslandId) == "starter-island" &&
        plan.Width == 1536 && plan.Depth == 1536 && plan.OriginX == 0 && plan.OriginZ == 0;
    public static string PartitionLabel(int index) => index is >= 0 and < 26 ? ((char)('A' + index)).ToString() : (index + 1).ToString();

    public static RealmIslandView[] Islands(RealmSummary realm)
    {
        if (realm.Topology is { } topology)
            return topology.Islands.Select(island => new RealmIslandView(island,
                realm.Servers.Where(server => server.IslandId == island.IslandId).OrderBy(server => server.PartitionIndex).ToArray())).ToArray();
        // The web beta is a fixed, single-island profile, without a topology snapshot.
        if (realm.ProfileId == "beta-web" && realm.Servers is [{ Id: "island" } server])
            return [new(new("starter-island", 1, 1536, 1536, GridColumns: 1, GridRows: 1), [server], true)];
        return [];
    }

    public static (int Columns, int Rows) Grid(RealmIslandTopology island)
    {
        if (island.GridColumns is > 0 and <= 64 && island.GridRows is > 0 and <= 64 &&
            island.GridColumns * island.GridRows == island.DedicatedCount)
            return (island.GridColumns.Value, island.GridRows.Value);
        // Preserve the count-based layout used by older saved plans.
        var columns = 1;
        var best = double.PositiveInfinity;
        for (var candidate = 1; candidate <= Math.Clamp(island.DedicatedCount, 1, 64); candidate++)
        {
            if (island.DedicatedCount % candidate != 0) continue;
            var score = Math.Abs(Math.Log((island.Width / candidate) / (island.Depth / (island.DedicatedCount / candidate))));
            if (score < best - 1e-12 || Math.Abs(score - best) <= 1e-12 && candidate > columns)
            { columns = candidate; best = score; }
        }
        return (columns, Math.Clamp(island.DedicatedCount / columns, 1, 64));
    }
}
