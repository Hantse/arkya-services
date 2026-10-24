using Arkya.Content.Api.Application;
using Arkya.Content.Api.Domain;

namespace Arkya.Content.Tests;

internal sealed class MemoryPostRepository : IPostRepository
{
    private readonly Dictionary<Guid, ContentPost> posts = [];
    private readonly object gate = new();
    public Task CreateAsync(ContentPost post, CancellationToken ct)
    {
        lock (gate) posts.Add(post.Id, post);
        return Task.CompletedTask;
    }
    public Task<ContentPost?> FindAsync(Guid id, CancellationToken ct)
    {
        lock (gate) return Task.FromResult(posts.GetValueOrDefault(id));
    }
    public Task<IReadOnlyList<ContentPost>> ListAsync(bool publishedOnly, string? kind, string? language, int limit, int offset,
        CancellationToken ct, PostFeedFilter? filter = null)
    {
        lock (gate)
        {
            var matches = posts.Values
                .Where(post => !publishedOnly || post.Published is not null)
                .Where(post => kind is null || (publishedOnly ? post.Published!.Content : post.Draft).Kind == kind)
                .Where(post => language is null || (publishedOnly ? post.Published!.Content : post.Draft).Language == language)
                .Where(post => filter is null ||
                    (filter.GameVersion is null || (publishedOnly ? post.Published!.Content : post.Draft).GameVersion == filter.GameVersion) &&
                    (filter.From is null || (publishedOnly ? post.Published!.PublishedAt : post.UpdatedAt) >= filter.From) &&
                    (filter.ToExclusive is null || (publishedOnly ? post.Published!.PublishedAt : post.UpdatedAt) < filter.ToExclusive) &&
                    (string.IsNullOrEmpty(filter.Query) || Matches(publishedOnly ? post.Published!.Content : post.Draft, filter.Query)));
            var sorted = filter?.OldestFirst == true
                ? matches.OrderBy(post => publishedOnly ? post.Published!.PublishedAt : post.UpdatedAt)
                    .ThenBy(post => post.Id.ToString("D"), StringComparer.Ordinal)
                : matches.OrderByDescending(post => publishedOnly ? post.Published!.PublishedAt : post.UpdatedAt)
                    .ThenByDescending(post => post.Id.ToString("D"), StringComparer.Ordinal);
            return Task.FromResult<IReadOnlyList<ContentPost>>(sorted.Skip(offset).Take(limit).ToArray());
        }
    }

    private static bool Matches(PostContent content, string query) => content.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        content.Summary.Contains(query, StringComparison.OrdinalIgnoreCase) || content.Body.Contains(query, StringComparison.OrdinalIgnoreCase);
    public Task<bool> TryReplaceAsync(long expectedRevision, ContentPost replacement, CancellationToken ct)
    {
        lock (gate)
        {
            if (!posts.TryGetValue(replacement.Id, out var existing) || existing.Revision != expectedRevision) return Task.FromResult(false);
            posts[replacement.Id] = replacement;
            return Task.FromResult(true);
        }
    }
    public Task<bool> TryDeleteAsync(Guid id, long expectedRevision, CancellationToken ct)
    {
        lock (gate) return Task.FromResult(posts.TryGetValue(id, out var existing) && existing.Revision == expectedRevision && posts.Remove(id));
    }
}
