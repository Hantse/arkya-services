using Arkya.ServiceDefaults;

namespace Arkya.Characters.Tests;

public sealed class GameProtocolCompatibilityTests
{
    [Theory]
    [InlineData(GameProtocol.Version, GameProtocol.BuildId, true, false)]
    [InlineData(GameProtocol.HandoffVersion, GameProtocol.HandoffBuildId, true, true)]
    [InlineData(GameProtocol.HandoffVersion, GameProtocol.CharacterStatsBuildId, true, true)]
    [InlineData(GameProtocol.MultiAreaVersion, GameProtocol.MultiAreaBuildId, true, true)]
    [InlineData(GameProtocol.DivingVersion, GameProtocol.DivingBuildId, true, true)]
    [InlineData(GameProtocol.TownNpcVersion, GameProtocol.TownNpcBuildId, true, true)]
    [InlineData(GameProtocol.CraftingVersion, GameProtocol.CraftingBuildId, true, true)]
    [InlineData(GameProtocol.CraftSchoolsVersion, GameProtocol.CraftSchoolsBuildId, true, true)]
    [InlineData(GameProtocol.NpcVitalsVersion, GameProtocol.NpcVitalsBuildId, true, true)]
    [InlineData(GameProtocol.NpcVitalsVersion, GameProtocol.CraftSchoolsBuildId, false, false)]
    [InlineData(GameProtocol.CraftSchoolsVersion, GameProtocol.NpcVitalsBuildId, false, false)]
    [InlineData(GameProtocol.NpcVitalsVersion, null, false, false)]
    [InlineData(GameProtocol.CraftSchoolsVersion, GameProtocol.CraftingBuildId, false, false)]
    [InlineData(GameProtocol.CraftingVersion, GameProtocol.CraftSchoolsBuildId, false, false)]
    [InlineData(GameProtocol.CraftSchoolsVersion, null, false, false)]
    [InlineData(GameProtocol.CraftingVersion, GameProtocol.TownNpcBuildId, false, false)]
    [InlineData(GameProtocol.TownNpcVersion, GameProtocol.CraftingBuildId, false, false)]
    [InlineData(GameProtocol.CraftingVersion, null, false, false)]
    [InlineData(GameProtocol.TownNpcVersion, GameProtocol.DivingBuildId, false, false)]
    [InlineData(GameProtocol.DivingVersion, GameProtocol.TownNpcBuildId, false, false)]
    [InlineData(GameProtocol.TownNpcVersion, null, false, false)]
    [InlineData(GameProtocol.DivingVersion, GameProtocol.MultiAreaBuildId, false, false)]
    [InlineData(GameProtocol.MultiAreaVersion, GameProtocol.DivingBuildId, false, false)]
    [InlineData(GameProtocol.DivingVersion, GameProtocol.CharacterStatsBuildId, false, false)]
    [InlineData(GameProtocol.DivingVersion, null, false, false)]
    [InlineData(GameProtocol.HandoffVersion, GameProtocol.MultiAreaBuildId, false, false)]
    [InlineData(GameProtocol.MultiAreaVersion, GameProtocol.HandoffBuildId, false, false)]
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
    [InlineData(GameProtocol.HandoffVersion, GameProtocol.HandoffBuildId)]
    [InlineData(GameProtocol.HandoffVersion, GameProtocol.CharacterStatsBuildId)]
    [InlineData(GameProtocol.MultiAreaVersion, GameProtocol.MultiAreaBuildId)]
    [InlineData(GameProtocol.DivingVersion, GameProtocol.DivingBuildId)]
    [InlineData(GameProtocol.TownNpcVersion, GameProtocol.TownNpcBuildId)]
    [InlineData(GameProtocol.CraftingVersion, GameProtocol.CraftingBuildId)]
    [InlineData(GameProtocol.CraftSchoolsVersion, GameProtocol.CraftSchoolsBuildId)]
    [InlineData(GameProtocol.NpcVitalsVersion, GameProtocol.NpcVitalsBuildId)]
    public void SupportedHandoffBuildsKeepTrustedPartitionRouting(int version, string buildId)
    {
        var entry = new GameRealmEntry("beta-web", "Test realm", true, "area-west", "wss://game.example/west",
            version, buildId)
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
