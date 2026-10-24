using Arkya.Content.Api.Domain;

namespace Arkya.Content.Api.Application;

public interface IPostRepository
{
    Task CreateAsync(ContentPost post, CancellationToken cancellationToken);
    Task<ContentPost?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ContentPost>> ListAsync(bool publishedOnly, string? kind, string? language, int limit, int offset,
        CancellationToken cancellationToken, PostFeedFilter? filter = null);
    Task<bool> TryReplaceAsync(long expectedRevision, ContentPost replacement, CancellationToken cancellationToken);
    Task<bool> TryDeleteAsync(Guid id, long expectedRevision, CancellationToken cancellationToken);
}
