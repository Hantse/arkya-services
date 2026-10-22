namespace Arkya.Content.Api.Domain;

public sealed record PostContent(string Kind, string Language, string Title, string Summary, string Body, string? GameVersion);
public sealed record PublishedPost(long Revision, DateTimeOffset PublishedAt, PostContent Content);
public sealed record ContentPost(Guid Id, long Revision, PostContent Draft, DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt, PublishedPost? Published);
