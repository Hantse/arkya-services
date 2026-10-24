namespace Arkya.Publications.Mcp;

public sealed record PublicationResponse(Guid Id, long Revision, string Kind, string Language, string Title,
    string Summary, string Body, string? GameVersion, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    DateTimeOffset? PublishedAt, long? PublishedRevision);

public sealed record PublicationListResponse(PublicationResponse[] Items, bool HasMore);

public sealed record CreatePublicationRequest(string Kind, string Language, string Title, string Summary,
    string Body, string? GameVersion);

public sealed record UpdatePublicationRequest(long Revision, string Kind, string Language, string Title,
    string Summary, string Body, string? GameVersion);

public sealed record DeletePublicationResponse(Guid Id, bool Deleted);
