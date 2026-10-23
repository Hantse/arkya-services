using Arkya.ServiceDefaults;

namespace Arkya.Characters.Tests;

public sealed class GameProtocolCompatibilityTests
{
    [Theory]
    [InlineData(GameProtocol.Version, GameProtocol.BuildId, true, false)]
    [InlineData(GameProtocol.HandoffVersion, GameProtocol.HandoffBuildId, true, true)]
    [InlineData(GameProtocol.HandoffVersion, GameProtocol.CharacterStatsBuildId, true, true)]
    [InlineData(GameProtocol.Version, GameProtocol.CharacterStatsBuildId, false, false)]
    [InlineData(GameProtocol.Version, GameProtocol.HandoffBuildId, false, false)]
    [InlineData(GameProtocol.HandoffVersion, GameProtocol.BuildId, false, false)]
    [InlineData(3, GameProtocol.CharacterStatsBuildId, false, false)]
    [InlineData(GameProtocol.HandoffVersion, "arkya-character-stats-v2", false, false)]
    [InlineData(GameProtocol.HandoffVersion, null, false, false)]
    public void OnlyExplicitWireVersionAndBuildPairsAreSupported(int version, string? buildId, bool supported, bool handoff)
    {
        Assert.Equal(supported, GameProtocol.IsSupported(version, buildId));
        Assert.Equal(handoff, GameProtocol.IsHandoff(version, buildId));
    }

    [Theory]
    [InlineData(GameProtocol.HandoffBuildId)]
    [InlineData(GameProtocol.CharacterStatsBuildId)]
    public void BothSupportedHandoffBuildsKeepTrustedPartitionRouting(string buildId)
    {
        var entry = new GameRealmEntry("beta-web", "Test realm", true, "area-west", "wss://game.example/west",
            GameProtocol.HandoffVersion, buildId)
        {
            TopologyVersion = "starter-island-v1",
            Partitions = [new("area-west", "wss://game.example/west", "starter-island", 0, new(0, 0, 500, 1000)),
                new("area-east", "wss://game.example/east", "starter-island", 1, new(500, 0, 1000, 1000))]
        };
        Assert.True(GameTopology.IsValid(entry));
        Assert.Equal("area-east", GameTopology.Resolve(entry, "starter-island", 500, 250)!.ServerId);
        Assert.False(GameTopology.IsValid(entry with { ProtocolVersion = GameProtocol.Version }));
    }
}
