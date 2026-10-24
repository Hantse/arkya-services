using Arkya.Realms.Api.Contracts;
using Arkya.Realms.Api.Domain;
using Microsoft.Extensions.Options;

namespace Arkya.Realms.Api.Application;

public sealed record RealmResult(RealmRecord? Realm = null, string? Error = null);

public sealed class RealmService(IRealmRepository repository, IOptions<DeploymentOptions> configured, TimeProvider clock)
{
    private readonly DeploymentOptions options = configured.Value;

    public DeploymentOptionsResponse GetOptions() => new(
        options.Targets.Select(target => new TargetSummary(target.Id, target.DisplayName, target.Namespace, target.RealmId)).ToArray(),
        options.Profiles.Select(profile => new ProfileSummary(profile.Id, profile.DisplayName, profile.Image,
            profile.Servers.Select(server => new ProfileServerSummary(server.Id, server.Port, server.Transport, server.PublicEndpoint)).ToArray())
            { TopologySupported = RealmTopologyBuilder.RuntimeSupported(profile) }).ToArray(), options.Enabled);

    public async Task<RealmResult> CreateAsync(CreateRealmRequest request, CancellationToken cancellationToken)
    {
        if (!DeploymentOptions.ValidId(request.RealmId) || !DeploymentOptions.ValidName(request.DisplayName))
            return new(Error: "invalid_realm");
        var target = options.Targets.SingleOrDefault(value => value.Id == request.TargetId);
        var profile = options.Profiles.SingleOrDefault(value => value.Id == request.ProfileId);
        if (target is null || profile is null) return new(Error: "invalid_configuration_choice");
        if (target.RealmId is not null && target.RealmId != request.RealmId) return new(Error: "target_realm_mismatch");
        if (request.Topology is not null && !RealmTopologyBuilder.IsValid(request.Topology)) return new(Error: "invalid_topology");
        ProfileSnapshot snapshot;
        try { snapshot = RealmTopologyBuilder.Snapshot(profile, request.RealmId, request.Topology); }
        catch (ArgumentException) { return new(Error: "invalid_topology_endpoint"); }
        var realm = new RealmRecord(request.RealmId, request.DisplayName, target.Id, target.Namespace, snapshot,
            Guid.NewGuid().ToString("N"), 0, 0, null, RealmStatus.Draft, null, clock.GetUtcNow(), null, []);
        return await repository.TryCreateAsync(realm, cancellationToken) ? new(realm) : new(Error: "realm_or_target_exists");
    }

    public async Task<RealmResult> UpdateTopologyAsync(string id, UpdateRealmTopologyRequest request, CancellationToken cancellationToken)
    {
        var realm = await repository.FindAsync(id, cancellationToken);
        if (realm is null || realm.Status == RealmStatus.Deleted) return new(Error: "realm_not_found");
        if (realm.Status != RealmStatus.Draft) return new(Error: "topology_edit_unavailable");
        if (request.ExpectedRevision < 0 || request.ExpectedRevision == long.MaxValue || realm.Revision != request.ExpectedRevision)
            return new(Error: "revision_conflict");
        var profile = options.Profiles.SingleOrDefault(value => value.Id == request.ProfileId);
        if (profile is null) return new(Error: "invalid_configuration_choice");
        if (request.Topology is not null && !RealmTopologyBuilder.IsValid(request.Topology)) return new(Error: "invalid_topology");
        ProfileSnapshot snapshot;
        try { snapshot = RealmTopologyBuilder.Snapshot(profile, realm.RealmId, request.Topology); }
        catch (ArgumentException) { return new(Error: "invalid_topology_endpoint"); }
        var next = realm with
        {
            Profile = snapshot,
            Revision = realm.Revision + 1, Version = realm.Version + 1, UpdatedAt = clock.GetUtcNow()
        };
        return await repository.TryReplaceAsync(next, realm.Version, cancellationToken) ? new(next) : new(Error: "revision_conflict");
    }

    public async Task<RealmResult> RequestDeploymentAsync(string id, long expectedRevision, CancellationToken cancellationToken)
    {
        if (!options.Enabled) return new(Error: "deployment_disabled");
        var realm = await repository.FindAsync(id, cancellationToken);
        if (realm is null || realm.Status == RealmStatus.Deleted) return new(Error: "realm_not_found");
        if (RealmStatus.IsDeleting(realm.Status)) return new(Error: "deletion_unavailable");
        if (!RealmTopologyBuilder.SnapshotSupportsRuntime(realm.Profile)) return new(Error: "topology_runtime_unsupported");
        // A response lost after the durable write may safely be retried with its original revision.
        if (realm.RequestedFromRevision == expectedRevision) return new(realm);
        if (expectedRevision < 0 || expectedRevision == long.MaxValue || realm.Revision != expectedRevision ||
            realm.Status is not (RealmStatus.Draft or RealmStatus.Failed)) return new(Error: "revision_conflict");
        if (!options.Targets.Any(target => target.Id == realm.TargetId && target.Namespace == realm.Namespace &&
            (target.RealmId is null || target.RealmId == realm.RealmId)))
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

    public RealmOperationsOptions GetOperationsOptions(RealmRecord realm)
    {
        var reason = !options.Enabled ? "deployment_disabled" : !TargetAvailable(realm) ? "target_unavailable" :
            !RealmTopologyBuilder.SnapshotSupportsRuntime(realm.Profile) ? "topology_runtime_unsupported" :
            realm.Status is not (RealmStatus.Ready or RealmStatus.Failed) ? "operation_in_progress" : null;
        var release = RealmImageReleases.Find(options, realm.RealmId);
        var profile = release is null ? null : RealmImageReleases.AllowedProfile(options, realm, release);
        var retry = realm.Status == RealmStatus.Failed && realm.ImageRelease?.Version == release?.Version &&
            RealmImageReleases.Authorizes(options, realm);
        var update = profile is not null && release is not null &&
            (retry || realm.Profile.Image == release.PreviousImage && profile.Image != realm.Profile.Image &&
                release.Version > (realm.ImageRelease?.Version ?? 0) &&
                (realm.ImageRelease is null || realm.Status == RealmStatus.Ready));
        var restart = (realm.ImageRelease is null || RealmImageReleases.Authorizes(options, realm)) &&
            !(update && !retry && release!.Automatic);
        var currentPair = ProtocolPair(realm.Profile);
        if (RealmRuntimeReleases.ManualApproval(options, realm) is { } migration)
        {
            var targetPair = ProtocolPair(migration.Profile);
            return new(reason is null && restart, reason is null, migration.Profile.Image, migration.Release.Version, reason,
                "RuntimeMigration", currentPair.Version, currentPair.BuildId, targetPair.Version, targetPair.BuildId,
                migration.Release.GameVersion, migration.Release.SourceRevision, migration.Release.SourceRunId);
        }
        var target = profile is null ? default : ProtocolPair(profile.Snapshot());
        var runtimeRelease = RealmRuntimeReleases.Find(options, realm.RealmId);
        var runtimeUnavailable = runtimeRelease is { Automatic: false } &&
            (realm.RuntimeRelease is { Completed: false } || runtimeRelease.Version > (realm.RuntimeRelease?.Version ?? 0));
        return new(reason is null && restart, reason is null && update,
            update ? profile!.Image : null, update ? release!.Version : null,
            reason ?? (update ? null : runtimeUnavailable ? "runtime_migration_unavailable" : "no_approved_update"),
            update ? "Image" : null, currentPair.Version, currentPair.BuildId,
            update ? target.Version : null, update ? target.BuildId : null);
    }

    public Task<RealmResult> RequestRestartAsync(string id, RestartRealmRequest request, string? actor,
        CancellationToken cancellationToken) => RequestOperationAsync(id, "Restart", request.ExpectedRevision,
            request.Reason, null, actor, cancellationToken);

    public Task<RealmResult> RequestUpdateAsync(string id, UpdateRealmRequest request, string? actor,
        CancellationToken cancellationToken) => RequestOperationAsync(id, "Update", request.ExpectedRevision,
            request.Reason, request.ExpectedImage, actor, cancellationToken);

    public async Task<RealmResult> RequestDeletionAsync(string id, DeleteRealmRequest request, string? actor,
        CancellationToken cancellationToken)
    {
        if (!DeploymentOptions.ValidId(id)) return new(Error: "invalid_realm");
        if (!ValidAuditText(actor, 128)) return new(Error: "invalid_operator");
        if (!ValidAuditText(request.Reason, 500)) return new(Error: "invalid_operation_reason");
        if (!string.Equals(request.ConfirmRealmId, id, StringComparison.Ordinal)) return new(Error: "invalid_delete_confirmation");
        var realm = await repository.FindAsync(id, cancellationToken);
        if (realm is null) return new(Error: "realm_not_found");
        if (IsRetry(realm)) return new(realm);
        if (realm.Status == RealmStatus.Deleted) return new(Error: "realm_not_found");
        if (request.ExpectedRevision < 0 || request.ExpectedRevision == long.MaxValue ||
            realm.Revision != request.ExpectedRevision || realm.Version == long.MaxValue)
            return new(Error: "revision_conflict");
        if (realm.Status is not (RealmStatus.Draft or RealmStatus.Ready or RealmStatus.Failed or RealmStatus.DeleteFailed))
            return new(Error: "deletion_unavailable");
        var draft = realm.Status == RealmStatus.Draft;
        if (!draft && !options.Enabled) return new(Error: "deletion_disabled");
        if (!draft && !TargetAvailable(realm)) return new(Error: "target_unavailable");
        var now = clock.GetUtcNow();
        var revision = realm.Revision + 1;
        var status = draft ? RealmStatus.Deleted : RealmStatus.Deleting;
        var next = realm with
        {
            Revision = revision, Version = realm.Version + 1, RequestedFromRevision = null,
            Status = status, LastError = null, UpdatedAt = now, DeploymentStartedAt = now,
            DeletedAt = draft ? now : null, LastObservedAt = null,
            Servers = realm.Profile.Servers.Select(server => new ServerObservation(server.Id, 0, null)).ToArray(),
            Operations = realm.Operations.Append(new RealmOperation("Delete", revision, request.ExpectedRevision,
                actor!, request.Reason, now, draft ? now : null, status, null, realm.Profile.Image, realm.Profile.Image))
                .TakeLast(100).ToArray()
        };
        if (await repository.TryReplaceAsync(next, realm.Version, cancellationToken)) return new(next);
        var current = await repository.FindAsync(id, cancellationToken);
        return current is not null && IsRetry(current) ? new(current) : new(Error: "revision_conflict");

        bool IsRetry(RealmRecord current) => current.Status is (RealmStatus.Deleting or RealmStatus.Deleted) &&
            current.Operations.Any(operation => operation.Kind == "Delete" && operation.ExpectedRevision == request.ExpectedRevision &&
                operation.RequestedBy == actor && operation.Reason == request.Reason);
    }

    private async Task<RealmResult> RequestOperationAsync(string id, string kind, long expectedRevision, string? reason,
        string? expectedImage, string? actor, CancellationToken cancellationToken)
    {
        if (!ValidAuditText(actor, 128)) return new(Error: "invalid_operator");
        if (!ValidAuditText(reason, 500)) return new(Error: "invalid_operation_reason");
        if (kind == "Update" && !DeploymentOptions.ValidImage(expectedImage)) return new(Error: "invalid_update_image");
        if (!options.Enabled) return new(Error: "deployment_disabled");
        var realm = await repository.FindAsync(id, cancellationToken);
        if (realm is null || realm.Status == RealmStatus.Deleted) return new(Error: "realm_not_found");
        if (RealmStatus.IsDeleting(realm.Status)) return new(Error: "deletion_unavailable");
        if (IsRetry(realm)) return new(realm);
        if (expectedRevision < 0 || expectedRevision == long.MaxValue || realm.Revision != expectedRevision)
            return new(Error: "revision_conflict");
        var available = GetOperationsOptions(realm);
        if (kind == "Restart" ? !available.CanRestart : !available.CanUpdate)
            return new(Error: available.UnavailableReason ?? "operation_unavailable");
        if (kind == "Update" && available.UpdateImage != expectedImage) return new(Error: "approved_image_changed");
        var now = clock.GetUtcNow();
        if (kind == "Update" && available.UpdateKind == "RuntimeMigration")
        {
            var migration = RealmRuntimeReleases.ManualApproval(options, realm)!.Value;
            var requested = RealmRuntimeReleases.BeginTransition(realm, migration.Release, migration.Profile, now, actor!, reason!);
            if (await repository.TryReplaceAsync(requested, realm.Version, cancellationToken)) return new(requested);
            var winner = await repository.FindAsync(id, cancellationToken);
            return winner is not null && IsRetry(winner) ? new(winner) : new(Error: "revision_conflict");
        }
        var revision = realm.Revision + 1;
        var next = realm with
        {
            Revision = revision, Version = realm.Version + 1, RequestedFromRevision = null,
            Status = RealmStatus.Pending, LastError = null, UpdatedAt = now, DeploymentStartedAt = now,
            Servers = realm.Profile.Servers.Select(server => new ServerObservation(server.Id, 0, null)).ToArray()
        };
        if (kind == "Restart") next = next with
        {
            Restart = realm.Status == RealmStatus.Failed && realm.Restart is { Completed: false }
                ? realm.Restart : new(revision, realm.Restart?.Revision ?? 0)
        };
        else if (realm.Profile.Image != available.UpdateImage)
        {
            var release = RealmImageReleases.Find(options, realm.RealmId)!;
            next = next with
            {
                Profile = realm.Profile with { Image = available.UpdateImage! },
                ImageRelease = new(release.Version, release.PreviousImage, available.UpdateImage!, realm.ImageRelease?.Version ?? 0)
            };
        }
        next = next with { Operations = realm.Operations.Append(new RealmOperation(kind, revision, expectedRevision,
            actor!, reason!, now, null, RealmStatus.Pending, null, realm.Profile.Image, next.Profile.Image)).TakeLast(100).ToArray() };
        if (await repository.TryReplaceAsync(next, realm.Version, cancellationToken)) return new(next);
        var current = await repository.FindAsync(id, cancellationToken);
        return current is not null && IsRetry(current) ? new(current) : new(Error: "revision_conflict");

        bool IsRetry(RealmRecord current) => current.Operations.Any(operation => operation.ExpectedRevision == expectedRevision &&
            (operation.Kind == kind || kind == "Update" && operation.Kind == "RuntimeMigration") && operation.RequestedBy == actor && operation.Reason == reason &&
            (kind != "Update" || operation.Image == expectedImage));
    }

    private bool TargetAvailable(RealmRecord realm) => options.Targets.Any(target => target.Id == realm.TargetId &&
        target.Namespace == realm.Namespace && (target.RealmId is null || target.RealmId == realm.RealmId));

    private static (int? Version, string? BuildId) ProtocolPair(ProfileSnapshot profile)
    {
        var pairs = profile.Servers.Select(server => (server.ProtocolVersion, server.BuildId)).Distinct().ToArray();
        return pairs.Length == 1 ? pairs[0] : default;
    }

    private static bool ValidAuditText(string? value, int limit) => value is { Length: > 0 } && value.Length <= limit &&
        value == value.Trim() && !value.Any(char.IsControl);

}
