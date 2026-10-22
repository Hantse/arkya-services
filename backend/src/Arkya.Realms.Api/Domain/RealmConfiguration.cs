namespace Arkya.Realms.Api.Domain;

public sealed record RealmRuntimeConfiguration
{
    public long Revision { get; init; }
    public decimal XpMultiplier { get; init; } = 1m;
    public decimal HarvestMultiplier { get; init; } = 1m;
    public DateTimeOffset? UpdatedAt { get; init; }
    public string? UpdatedBy { get; init; }
    public RealmConfigurationChange[] History { get; init; } = [];
}

public sealed record RealmConfigurationChange(long Revision, long ExpectedRevision, decimal XpMultiplier,
    decimal HarvestMultiplier, decimal PreviousXpMultiplier, decimal PreviousHarvestMultiplier,
    DateTimeOffset UpdatedAt, string UpdatedBy, string Reason);
