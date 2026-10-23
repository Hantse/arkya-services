using Arkya.Content.Api.Domain;

namespace Arkya.Content.Api.Contracts;

public sealed record CreatePostRequest(string? Kind, string? Language, string? Title, string? Summary, string? Body, string? GameVersion);
public sealed record UpdatePostRequest(long? Revision, string? Kind, string? Language, string? Title, string? Summary, string? Body, string? GameVersion);
public sealed record PostRevisionRequest(long? Revision);
public sealed record PostPage<T>(IReadOnlyList<T> Items, bool HasMore);

public sealed record AdminPostResponse(Guid Id, long Revision, string Kind, string Language, string Title, string Summary,
    string Body, string? GameVersion, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? PublishedAt, long? PublishedRevision)
{
    public static AdminPostResponse From(ContentPost post) => new(post.Id, post.Revision, post.Draft.Kind, post.Draft.Language,
        post.Draft.Title, post.Draft.Summary, post.Draft.Body, post.Draft.GameVersion, post.CreatedAt, post.UpdatedAt,
        post.Published?.PublishedAt, post.Published?.Revision);
}

public sealed record PublicPostResponse(Guid Id, string Kind, string Language, string Title, string Summary,
    string Body, string? GameVersion, DateTimeOffset PublishedAt)
{
    public static PublicPostResponse From(ContentPost post)
    {
        var published = post.Published!;
        return new(post.Id, published.Content.Kind, published.Content.Language, published.Content.Title,
            published.Content.Summary, published.Content.Body, published.Content.GameVersion, published.PublishedAt);
    }
}
