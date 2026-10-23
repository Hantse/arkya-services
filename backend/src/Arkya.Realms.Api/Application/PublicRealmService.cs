using Arkya.Realms.Api.Contracts;
using Arkya.Realms.Api.Domain;
using Microsoft.Extensions.Options;

namespace Arkya.Realms.Api.Application;

public sealed class PublicRealmService(IRealmRepository realms, IOptions<DeploymentOptions> configured,
    TimeProvider clock)
{
    public async Task<PublicRealmSummary[]> ListAsync(CancellationToken cancellationToken) =>
        (await realms.ListAsync(cancellationToken)).Where(IsPublic)
            .OrderBy(realm => realm.RealmId, StringComparer.Ordinal).Select(Summary).ToArray();

    public async Task<PublicRealmSummary?> FindAsync(string id, CancellationToken cancellationToken)
    {
        if (!DeploymentOptions.ValidId(id)) return null;
        var realm = await realms.FindAsync(id, cancellationToken);
        return realm is not null && IsPublic(realm) ? Summary(realm) : null;
    }

    private static bool IsPublic(RealmRecord realm) => DeploymentOptions.ValidId(realm.RealmId) &&
        realm.RealmId is not ("api" or "admin" or "www" or "backoffice" or "beta-game") &&
        realm.Status is RealmStatus.Pending or RealmStatus.Deploying or RealmStatus.Ready or RealmStatus.Failed;

    private PublicRealmSummary Summary(RealmRecord realm)
    {
        var now = clock.GetUtcNow();
        var lastObserved = realm.LastObservedAt is { } observed && observed <= now ? observed : (DateTimeOffset?)null;
        var fresh = lastObserved is { } last &&
            now - last <= TimeSpan.FromSeconds(Math.Max(30, configured.Value.ReconcileIntervalSeconds * 3));
        var ready = fresh ? realm.Profile.Servers.Count(server => realm.Servers.Any(observation =>
            observation.Id == server.Id && observation.ReadyReplicas == 1 &&
            !string.IsNullOrWhiteSpace(observation.Address) &&
            (realm.ImageRelease is null || observation.ReadyImage == realm.Profile.Image) &&
            (realm.Restart is null || observation.ReadyRestartRevision == realm.Restart.Revision))) : 0;
        return new(realm.RealmId, realm.DisplayName,
            realm.Status == RealmStatus.Ready && !fresh ? "Unknown" : realm.Status,
            ready, realm.Profile.Servers.Length, realm.UpdatedAt, lastObserved,
            // Kubernetes readiness cannot establish authoritative player population.
            PlayerCount: null);
    }
}
