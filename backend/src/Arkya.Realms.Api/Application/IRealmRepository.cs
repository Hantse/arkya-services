using Arkya.Realms.Api.Domain;

namespace Arkya.Realms.Api.Application;

public interface IRealmRepository
{
    Task InitializeAsync(CancellationToken cancellationToken);
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<RealmRecord>> ListAsync(CancellationToken cancellationToken);
    Task<RealmRecord?> FindAsync(string realmId, CancellationToken cancellationToken);
    Task<bool> TryCreateAsync(RealmRecord realm, CancellationToken cancellationToken);
    Task<bool> TryReplaceAsync(RealmRecord realm, long expectedVersion, CancellationToken cancellationToken);
}

public interface IRealmDeployer
{
    Task<ServerObservation[]> ReconcileAsync(RealmRecord realm, CancellationToken cancellationToken);
}

public sealed class DeploymentFailure(string code) : Exception(code)
{
    public string Code { get; } = code;
}
