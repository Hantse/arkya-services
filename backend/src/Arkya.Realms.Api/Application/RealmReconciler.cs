using Arkya.Realms.Api.Domain;
using Microsoft.Extensions.Options;

namespace Arkya.Realms.Api.Application;

public sealed class RealmReconciler(IRealmRepository repository, IRealmDeployer deployer,
    IOptions<DeploymentOptions> configured, TimeProvider clock)
{
    private readonly DeploymentOptions options = configured.Value;

    public async Task ReconcileAsync(RealmRecord realm, CancellationToken cancellationToken)
    {
        if (!options.Enabled || realm.Status is not (RealmStatus.Pending or RealmStatus.Deploying or RealmStatus.Ready)) return;
        var now = clock.GetUtcNow();
        var next = realm;
        try
        {
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attempt.CancelAfter(TimeSpan.FromSeconds(30));
            var observations = await deployer.ReconcileAsync(realm, attempt.Token);
            var ready = observations.Length == realm.Profile.Servers.Length && observations.All(value =>
                value.ReadyReplicas == 1 && !string.IsNullOrWhiteSpace(value.Address));
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
                foreach (var realm in realms.Where(value => value.Status is RealmStatus.Pending or RealmStatus.Deploying or RealmStatus.Ready))
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
