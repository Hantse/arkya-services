using Arkya.Content.Api.Domain;
using MongoDB.Bson.Serialization.Attributes;

namespace Arkya.Content.Api.Infrastructure.Persistence;

internal sealed class PostDocument
{
    [BsonId] public string Id { get; set; } = "";
    public int SchemaVersion { get; set; } = 1;
    public long Revision { get; set; }
    public PostContentDocument Draft { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public PublishedPostDocument? Published { get; set; }

    public ContentPost ToDomain() => new(Guid.Parse(Id), Revision, Draft.ToDomain(), Utc(CreatedAt), Utc(UpdatedAt),
        Published is null ? null : new(Published.Revision, Utc(Published.PublishedAt), Published.Content.ToDomain()));
    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    public static PostDocument From(ContentPost post) => new()
    {
        Id = post.Id.ToString("D"), Revision = post.Revision, Draft = PostContentDocument.From(post.Draft),
        CreatedAt = post.CreatedAt.UtcDateTime, UpdatedAt = post.UpdatedAt.UtcDateTime,
        Published = post.Published is null ? null : new()
        {
            Revision = post.Published.Revision, PublishedAt = post.Published.PublishedAt.UtcDateTime,
            Content = PostContentDocument.From(post.Published.Content)
        }
    };
}

internal sealed class PublishedPostDocument
{
    public long Revision { get; set; }
    public DateTime PublishedAt { get; set; }
    public PostContentDocument Content { get; set; } = new();
}

internal sealed class PostContentDocument
{
    public string Kind { get; set; } = "";
    public string Language { get; set; } = "";
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Body { get; set; } = "";
    public string? GameVersion { get; set; }

    public PostContent ToDomain() => new(Kind, Language, Title, Summary, Body, GameVersion);
    public static PostContentDocument From(PostContent content) => new()
    {
        Kind = content.Kind, Language = content.Language, Title = content.Title, Summary = content.Summary,
        Body = content.Body, GameVersion = content.GameVersion
    };
}
