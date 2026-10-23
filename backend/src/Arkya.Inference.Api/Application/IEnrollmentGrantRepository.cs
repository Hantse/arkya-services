using Arkya.Inference.Api.Domain;

namespace Arkya.Inference.Api.Application;

public interface IEnrollmentGrantRepository
{
    Task InitializeAsync(CancellationToken cancellationToken);
    Task<EnrollmentGrantRecord?> FindAsync(string tokenHash, CancellationToken cancellationToken);
    Task<bool> TryCreateAsync(EnrollmentGrantRecord grant, CancellationToken cancellationToken);
    Task<bool> TryBindAsync(string tokenHash, string agentId, string agentTokenHash, DateTimeOffset now,
        CancellationToken cancellationToken);
    Task RevokeForAgentAsync(string agentId, string agentTokenHash, CancellationToken cancellationToken);
}
