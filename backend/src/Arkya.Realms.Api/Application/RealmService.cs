using Arkya.Realms.Api.Contracts;
using Arkya.Realms.Api.Domain;
using Microsoft.Extensions.Options;

namespace Arkya.Realms.Api.Application;

public sealed record RealmResult(RealmRecord? Realm = null, string? Error = null);

public sealed class RealmService(IRealmRepository repository, IOptions<DeploymentOptions> configured, TimeProvider clock)
{
    private readonly DeploymentOptions options = configured.Value;

    public DeploymentOptionsResponse GetOptions() => new(
        options.Targets.Select(target => new TargetSummary(target.Id, target.DisplayName, target.Namespace)).ToArray(),
        options.Profiles.Select(profile => new ProfileSummary(profile.Id, profile.DisplayName, profile.Image,
            profile.Servers.Select(server => new ProfileServerSummary(server.Id, server.Port)).ToArray())).ToArray(), options.Enabled);

    public async Task<RealmResult> CreateAsync(CreateRealmRequest request, CancellationToken cancellationToken)
    {
        if (!DeploymentOptions.ValidId(request.RealmId) || !DeploymentOptions.ValidName(request.DisplayName))
            return new(Error: "invalid_realm");
        var target = options.Targets.SingleOrDefault(value => value.Id == request.TargetId);
        var profile = options.Profiles.SingleOrDefault(value => value.Id == request.ProfileId);
        if (target is null || profile is null) return new(Error: "invalid_configuration_choice");
        var realm = new RealmRecord(request.RealmId, request.DisplayName, target.Id, target.Namespace, profile.Snapshot(),
            Guid.NewGuid().ToString("N"), 0, 0, null, RealmStatus.Draft, null, clock.GetUtcNow(), null, []);
        return await repository.TryCreateAsync(realm, cancellationToken) ? new(realm) : new(Error: "realm_or_target_exists");
    }

    public async Task<RealmResult> RequestDeploymentAsync(string id, long expectedRevision, CancellationToken cancellationToken)
    {
        if (!options.Enabled) return new(Error: "deployment_disabled");
        var realm = await repository.FindAsync(id, cancellationToken);
        if (realm is null) return new(Error: "realm_not_found");
        // A response lost after the durable write may safely be retried with its original revision.
        if (realm.RequestedFromRevision == expectedRevision) return new(realm);
        if (expectedRevision < 0 || expectedRevision == long.MaxValue || realm.Revision != expectedRevision ||
            realm.Status is not (RealmStatus.Draft or RealmStatus.Failed)) return new(Error: "revision_conflict");
        if (!options.Targets.Any(target => target.Id == realm.TargetId && target.Namespace == realm.Namespace))
            return new(Error: "target_unavailable");
        var requested = realm with
        {
            Revision = realm.Revision + 1, Version = realm.Version + 1, RequestedFromRevision = expectedRevision,
            Status = RealmStatus.Pending, LastError = null, UpdatedAt = clock.GetUtcNow(), DeploymentStartedAt = clock.GetUtcNow()
        };
        if (await repository.TryReplaceAsync(requested, realm.Version, cancellationToken)) return new(requested);
        var current = await repository.FindAsync(id, cancellationToken);
        return current?.RequestedFromRevision == expectedRevision ? new(current) : new(Error: "revision_conflict");
    }
}
