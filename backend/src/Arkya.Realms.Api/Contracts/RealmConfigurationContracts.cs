using Arkya.Realms.Api.Domain;

namespace Arkya.Realms.Api.Contracts;

public sealed record UpdateRealmConfigurationRequest(long ExpectedRevision, decimal XpMultiplier,
    decimal HarvestMultiplier, string Reason);
public sealed record RealmConfigurationSnapshot(long Revision, decimal XpMultiplier, decimal HarvestMultiplier)
{
    public static RealmConfigurationSnapshot From(RealmRuntimeConfiguration configuration) =>
        new(configuration.Revision, configuration.XpMultiplier, configuration.HarvestMultiplier);
}
public sealed record RealmConfigurationChangeSummary(long Revision, long ExpectedRevision, decimal XpMultiplier,
    decimal HarvestMultiplier, decimal PreviousXpMultiplier, decimal PreviousHarvestMultiplier,
    DateTimeOffset UpdatedAt, string UpdatedBy, string Reason);
public sealed record RealmConfigurationResponse(long Revision, decimal XpMultiplier, decimal HarvestMultiplier,
    DateTimeOffset? UpdatedAt, string? UpdatedBy, RealmConfigurationChangeSummary[] History)
{
    public static RealmConfigurationResponse From(RealmRuntimeConfiguration configuration) =>
        new(configuration.Revision, configuration.XpMultiplier, configuration.HarvestMultiplier,
            configuration.UpdatedAt, configuration.UpdatedBy, configuration.History.Select(change =>
                new RealmConfigurationChangeSummary(change.Revision, change.ExpectedRevision, change.XpMultiplier,
                    change.HarvestMultiplier, change.PreviousXpMultiplier, change.PreviousHarvestMultiplier,
                    change.UpdatedAt, change.UpdatedBy, change.Reason)).ToArray());
}
