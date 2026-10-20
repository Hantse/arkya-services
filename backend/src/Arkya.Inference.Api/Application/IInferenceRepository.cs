using Arkya.Inference.Api.Domain;

namespace Arkya.Inference.Api.Application;

public interface IInferenceRepository
{
    Task InitializeAsync(CancellationToken cancellationToken);
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<InferenceAgentRecord>> ListAsync(CancellationToken cancellationToken);
    Task<InferenceAgentRecord?> FindAsync(string agentId, CancellationToken cancellationToken);
    Task<bool> TryCreateAsync(InferenceAgentRecord agent, CancellationToken cancellationToken);
    Task<bool> TryReplaceAsync(InferenceAgentRecord agent, long expectedVersion, CancellationToken cancellationToken);
    Task<bool> TryDeleteAsync(string agentId, long expectedVersion, string expectedTokenHash, CancellationToken cancellationToken);
}
