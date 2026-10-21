using Arkya.Realms.Api.Domain;
using Microsoft.Extensions.Options;

namespace Arkya.Realms.Api.Application;

public sealed class RealmReconciler(IRealmRepository repository, IRealmDeployer deployer,
    IOptions<DeploymentOptions> configured, TimeProvider clock)
{
    private readonly DeploymentOptions options = configured.Value;

    public async Task ReconcileAsync(RealmRecord realm, CancellationToken cancellationToken)
    {
        if (!options.Enabled) return;
        var prepared = await PrepareImageReleaseAsync(realm, cancellationToken);
        if (prepared is null) return;
        realm = prepared;
        if (realm.Status is not (RealmStatus.Pending or RealmStatus.Deploying or RealmStatus.Ready) &&
            !(realm.Status == RealmStatus.Failed && RealmImageReleases.Authorizes(options, realm))) return;
        var now = clock.GetUtcNow();
        var next = realm;
        try
        {
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attempt.CancelAfter(TimeSpan.FromSeconds(30));
            var observations = await deployer.ReconcileAsync(realm, attempt.Token);
            var ready = observations.Length == realm.Profile.Servers.Length && observations.All(value =>
                value.ReadyReplicas == 1 && !string.IsNullOrWhiteSpace(value.Address) &&
                (realm.ImageRelease is null || value.ReadyImage == realm.Profile.Image));
            var start = realm.Status == RealmStatus.Ready && !ready ? now : realm.DeploymentStartedAt ?? now;
            next = realm with
            {
                Status = ready ? RealmStatus.Ready : now - start >= TimeSpan.FromSeconds(options.DeploymentTimeoutSeconds)
                    ? RealmStatus.Failed : RealmStatus.Deploying,
                LastError = !ready && now - start >= TimeSpan.FromSeconds(options.DeploymentTimeoutSeconds) ? "deployment_timeout" : null,
                Servers = observations, DeploymentStartedAt = start
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (DeploymentFailure failure) when (failure.Code != "cluster_unavailable")
        {
            next = realm with { Status = RealmStatus.Failed, LastError = failure.Code,
                Servers = realm.Profile.Servers.Select(server => new ServerObservation(server.Id, 0, null)).ToArray() };
        }
        catch (Exception exception) when (exception is DeploymentFailure or HttpRequestException or IOException or
            System.Text.Json.JsonException or OperationCanceledException)
        {
            var start = realm.Status == RealmStatus.Ready ? now : realm.DeploymentStartedAt ?? now;
            next = realm with
            {
                Status = now - start >= TimeSpan.FromSeconds(options.DeploymentTimeoutSeconds) ? RealmStatus.Failed : RealmStatus.Deploying,
                LastError = "cluster_unavailable", DeploymentStartedAt = start,
                Servers = realm.Profile.Servers.Select(server => new ServerObservation(server.Id, 0, null)).ToArray()
            };
        }
        if (realm.Status == next.Status && realm.LastError == next.LastError &&
            realm.DeploymentStartedAt == next.DeploymentStartedAt && realm.Servers.SequenceEqual(next.Servers)) return;
        using var saveTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        saveTimeout.CancelAfter(TimeSpan.FromSeconds(10));
        await repository.TryReplaceAsync(next with { Version = realm.Version + 1, UpdatedAt = now }, realm.Version, saveTimeout.Token);
    }

    private async Task<RealmRecord?> PrepareImageReleaseAsync(RealmRecord realm, CancellationToken cancellationToken)
    {
        var release = RealmImageReleases.Find(options, realm.RealmId);
        if (realm.ImageRelease is { } existing && (release is null || release.Version <= existing.Version))
            return release?.Version == existing.Version && RealmImageReleases.Authorizes(options, realm) ? realm : null;
        if (realm.ImageRelease is not null && realm.Status != RealmStatus.Ready) return null;
        if (release is null || realm.Status is not (RealmStatus.Ready or RealmStatus.Failed)) return realm;
        var profile = RealmImageReleases.AllowedProfile(options, realm, release);
        // A realm created after activation already starts with the current profile image.
        if (realm.ImageRelease is null && profile?.Image == realm.Profile.Image) return realm;
        if (profile is null || profile.Image == release.PreviousImage || realm.Profile.Image != release.PreviousImage ||
            realm.Revision == long.MaxValue) return null;
        var now = clock.GetUtcNow();
        var requested = realm with
        {
            Profile = realm.Profile with { Image = profile.Image },
            ImageRelease = new(release.Version, release.PreviousImage, profile.Image, realm.ImageRelease?.Version ?? 0),
            Revision = realm.Revision + 1, Version = realm.Version + 1, RequestedFromRevision = null,
            Status = RealmStatus.Pending, LastError = null, UpdatedAt = now, DeploymentStartedAt = now,
            Servers = realm.Profile.Servers.Select(server => new ServerObservation(server.Id, 0, null)).ToArray()
        };
        using var saveTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        saveTimeout.CancelAfter(TimeSpan.FromSeconds(10));
        return await repository.TryReplaceAsync(requested, realm.Version, saveTimeout.Token) ? requested : null;
    }
}

public sealed class RealmDeploymentWorker(IRealmRepository repository, RealmReconciler reconciler,
    IOptions<DeploymentOptions> configured, ILogger<RealmDeploymentWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configured.Value.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var queryTimeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                queryTimeout.CancelAfter(TimeSpan.FromSeconds(10));
                var realms = await repository.ListAsync(queryTimeout.Token);
                foreach (var realm in realms.Where(value => value.Status is RealmStatus.Pending or RealmStatus.Deploying or RealmStatus.Ready ||
                    value.Status == RealmStatus.Failed && RealmImageReleases.Find(configured.Value, value.RealmId) is not null))
                {
                    try { await reconciler.ReconcileAsync(realm, stoppingToken); }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                    catch (Exception)
                    {
                        // Never log dependency exception bodies, connection strings or projected tokens.
                        logger.LogWarning("Realm reconciliation was interrupted; the persisted operation will be retried.");
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception) { logger.LogWarning("Realm reconciliation store is unavailable; polling will retry."); }
            try { await Task.Delay(TimeSpan.FromSeconds(configured.Value.ReconcileIntervalSeconds), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }
}
