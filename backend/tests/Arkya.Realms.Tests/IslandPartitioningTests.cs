using Arkya.Realms.Api.Application;
using Arkya.Realms.Api.Domain;

namespace Arkya.Realms.Tests;

public sealed class IslandPartitioningTests
{
    [Fact]
    public void FourDedicatedServersDivideTheThousandUnitIslandIntoTheRequestedQuadrants()
    {
        var island = new RealmIslandTopology("starter-island", 4, 1000, 1000);
        Assert.Equal(new AreaBounds[]
        {
            new(0, 0, 500, 500), new(500, 0, 1000, 500),
            new(0, 500, 500, 1000), new(500, 500, 1000, 1000)
        }, IslandPartitioning.Create(island));
        Assert.Equal(0, IslandPartitioning.FindPartition(island, 250, 250));
        Assert.Equal(1, IslandPartitioning.FindPartition(island, 750, 250));
        Assert.Equal(2, IslandPartitioning.FindPartition(island, 250, 750));
        Assert.Equal(3, IslandPartitioning.FindPartition(island, 750, 750));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(499.999, 250, 0)]
    [InlineData(500, 250, 1)]
    [InlineData(250, 500, 2)]
    [InlineData(500, 500, 3)]
    [InlineData(1000, 0, 1)]
    [InlineData(0, 1000, 2)]
    [InlineData(1000, 1000, 3)]
    public void EveryInternalEdgeAndOuterCornerHasOneDeterministicOwner(double x, double z, int owner) =>
        Assert.Equal(owner, IslandPartitioning.FindPartition(new("starter-island", 4), x, z));

    [Theory]
    [InlineData(-0.001, 250)]
    [InlineData(250, -0.001)]
    [InlineData(1000.001, 250)]
    [InlineData(250, 1000.001)]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 0)]
    public void CoordinatesOutsideTheIslandCannotAcquireAnAuthority(double x, double z) =>
        Assert.Null(IslandPartitioning.FindPartition(new("starter-island", 4), x, z));

    [Fact]
    public void EverySupportedCountCoversTheEntireIslandWithoutOverlapsOrEmptyPartitions()
    {
        foreach (var (width, depth) in new[] { (1000d, 1000d), (2700d, 900d), (777d, 2345d) })
        for (var count = 1; count <= 64; count++)
        {
            var island = new RealmIslandTopology("future-island", count, width, depth, -1500, -750);
            var bounds = IslandPartitioning.Create(island);
            Assert.Equal(count, bounds.Length);
            Assert.Equal(width * depth, bounds.Sum(cell => (cell.MaxX - cell.MinX) * (cell.MaxZ - cell.MinZ)), 5);
            for (var index = 0; index < bounds.Length; index++)
            {
                var cell = bounds[index];
                Assert.True(cell.MinX < cell.MaxX && cell.MinZ < cell.MaxZ);
                Assert.Equal(index, IslandPartitioning.FindPartition(island, cell.MinX, cell.MinZ));
                Assert.Equal(index, IslandPartitioning.FindPartition(island, (cell.MinX + cell.MaxX) / 2, (cell.MinZ + cell.MaxZ) / 2));
                foreach (var other in bounds.Skip(index + 1))
                    Assert.False(Math.Max(cell.MinX, other.MinX) < Math.Min(cell.MaxX, other.MaxX) &&
                        Math.Max(cell.MinZ, other.MinZ) < Math.Min(cell.MaxZ, other.MaxZ));
            }
            for (var x = 0; x <= 20; x++)
            for (var z = 0; z <= 20; z++)
                Assert.NotNull(IslandPartitioning.FindPartition(island, island.OriginX + width * x / 20, island.OriginZ + depth * z / 20));
        }
    }

    [Theory]
    [InlineData(2000, 1000, 1000, 1000)]
    [InlineData(1000, 2000, 1000, 1000)]
    public void GridOrientationFollowsTheIslandAspectRatio(double width, double depth, double cellWidth, double cellDepth)
    {
        var bounds = IslandPartitioning.Create(new("long-island", 2, width, depth));
        Assert.All(bounds, cell =>
        {
            Assert.Equal(cellWidth, cell.MaxX - cell.MinX);
            Assert.Equal(cellDepth, cell.MaxZ - cell.MinZ);
        });
    }

    [Fact]
    public void NeighborDiscoveryIncludesSharedEdgesAndExcludesDiagonalCorners()
    {
        var bounds = IslandPartitioning.Create(new("starter-island", 4));
        Assert.True(IslandPartitioning.AreNeighbors(bounds[0], bounds[1]));
        Assert.True(IslandPartitioning.AreNeighbors(bounds[0], bounds[2]));
        Assert.False(IslandPartitioning.AreNeighbors(bounds[0], bounds[3]));
        Assert.False(IslandPartitioning.AreNeighbors(bounds[0], bounds[0]));
        Assert.True(IslandPartitioning.AreNeighbors(bounds[1], bounds[3]));
    }

    [Fact]
    public void FractionalDimensionsKeepExactSharedEdgesAndTheInclusiveOuterMaximum()
    {
        for (var count = 1; count <= 64; count++)
        {
            var island = new RealmIslandTopology("fractional-island", count, 1000.1, 987.6, 0.3, -0.7);
            var bounds = IslandPartitioning.Create(island);
            Assert.Equal(island.OriginX + island.Width, bounds[^1].MaxX);
            Assert.Equal(island.OriginZ + island.Depth, bounds[^1].MaxZ);
            Assert.Equal(count - 1, IslandPartitioning.FindPartition(island, island.OriginX + island.Width, island.OriginZ + island.Depth));
            for (var index = 0; index < bounds.Length; index++)
                Assert.Equal(index, IslandPartitioning.FindPartition(island, bounds[index].MinX, bounds[index].MinZ));
        }
    }

    [Theory]
    [InlineData(0, 1000, 1000, 0, 0)]
    [InlineData(65, 1000, 1000, 0, 0)]
    [InlineData(4, 0, 1000, 0, 0)]
    [InlineData(4, 1000, -1, 0, 0)]
    [InlineData(4, 1000, double.PositiveInfinity, 0, 0)]
    [InlineData(4, double.NaN, 1000, 0, 0)]
    [InlineData(4, 1000, 1000, double.NaN, 0)]
    [InlineData(4, 1000, 1000, 0, 10000001)]
    [InlineData(4, 0.000000001, 1000, 10000000, 0)]
    public void InvalidOrDegenerateIslandDimensionsAreRejected(int count, double width, double depth, double originX, double originZ)
    {
        var island = new RealmIslandTopology("starter-island", count, width, depth, originX, originZ);
        Assert.False(IslandPartitioning.IsValid(island));
        Assert.Throws<ArgumentOutOfRangeException>(() => IslandPartitioning.Create(island));
    }
}
