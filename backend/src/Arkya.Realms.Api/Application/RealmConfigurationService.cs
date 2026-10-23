using Arkya.Realms.Api.Contracts;
using Arkya.Realms.Api.Domain;

namespace Arkya.Realms.Api.Application;

public sealed record RealmConfigurationResult(RealmConfigurationResponse? Configuration = null, string? Error = null);

public sealed class RealmConfigurationService(IRealmRepository repository, TimeProvider clock)
{
    public async Task<RealmConfigurationResponse?> GetAsync(string realmId, CancellationToken cancellationToken)
    {
        var realm = await repository.FindAsync(realmId, cancellationToken);
        return realm is null || realm.Status == RealmStatus.Deleted ? null : RealmConfigurationResponse.From(realm.RuntimeConfiguration);
    }

    public async Task<RealmConfigurationSnapshot?> GetSnapshotAsync(string realmId, CancellationToken cancellationToken)
    {
        var realm = await repository.FindAsync(realmId, cancellationToken);
        return realm is null || RealmStatus.IsDeleting(realm.Status) ? null : RealmConfigurationSnapshot.From(realm.RuntimeConfiguration);
    }

    public async Task<RealmConfigurationResult> UpdateAsync(string realmId, UpdateRealmConfigurationRequest request,
        string? actor, CancellationToken cancellationToken)
    {
        if (!Printable(actor, 128)) return new(Error: "invalid_operator");
        if (!Printable(request.Reason, 500)) return new(Error: "invalid_operation_reason");
        if (request.ExpectedRevision < 0 || request.ExpectedRevision == long.MaxValue ||
            !ValidMultiplier(request.XpMultiplier) || !ValidMultiplier(request.HarvestMultiplier))
            return new(Error: "invalid_configuration");
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var realm = await repository.FindAsync(realmId, cancellationToken);
            if (realm is null || realm.Status == RealmStatus.Deleted) return new(Error: "realm_not_found");
            if (RealmStatus.IsDeleting(realm.Status)) return new(Error: "configuration_revision_conflict");
            var current = realm.RuntimeConfiguration;
            // Retried responses share the original configuration revision and complete audit identity.
            if (current.History.Any(change => change.ExpectedRevision == request.ExpectedRevision &&
                change.XpMultiplier == request.XpMultiplier && change.HarvestMultiplier == request.HarvestMultiplier &&
                change.UpdatedBy == actor && change.Reason == request.Reason))
                return new(RealmConfigurationResponse.From(current));
            if (current.Revision != request.ExpectedRevision || realm.Version == long.MaxValue)
                return new(Error: "configuration_revision_conflict");
            var now = clock.GetUtcNow();
            var revision = current.Revision + 1;
            var change = new RealmConfigurationChange(revision, current.Revision, request.XpMultiplier,
                request.HarvestMultiplier, current.XpMultiplier, current.HarvestMultiplier, now, actor!, request.Reason);
            var configuration = new RealmRuntimeConfiguration
            {
                Revision = revision, XpMultiplier = request.XpMultiplier, HarvestMultiplier = request.HarvestMultiplier,
                UpdatedAt = now, UpdatedBy = actor, History = current.History.Append(change).TakeLast(100).ToArray()
            };
            // Only the storage CAS version changes; game tuning must not request a workload rollout.
            if (await repository.TryReplaceAsync(realm with { Version = realm.Version + 1, RuntimeConfiguration = configuration },
                realm.Version, cancellationToken)) return new(RealmConfigurationResponse.From(configuration));
        }
        return new(Error: "configuration_revision_conflict");
    }

    private static bool ValidMultiplier(decimal value) => value is >= 0m and <= 100m && decimal.Round(value, 3) == value;
    private static bool Printable(string? value, int maximum) => value is { Length: > 0 } && value.Length <= maximum &&
        value == value.Trim() && !value.Any(char.IsControl);
}
