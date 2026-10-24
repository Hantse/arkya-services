using System.Text.RegularExpressions;
using Arkya.Content.Api.Application;
using Arkya.Content.Api.Domain;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Arkya.Content.Api.Infrastructure.Persistence;

public sealed class MongoPostRepository(IMongoClient client, IOptions<MongoOptions> options) : IPostRepository
{
    private readonly IMongoCollection<PostDocument> posts = client.GetDatabase(options.Value.DatabaseName)
        .GetCollection<PostDocument>("content_posts");
    private static readonly FilterDefinition<PostDocument> Supported = Builders<PostDocument>.Filter.Eq(post => post.SchemaVersion, 1);

    public Task InitializeAsync(CancellationToken ct) => posts.Indexes.CreateManyAsync([
        new CreateIndexModel<PostDocument>(Builders<PostDocument>.IndexKeys.Ascending(post => post.SchemaVersion)
            .Ascending(post => post.Published!.Content.Language).Ascending(post => post.Published!.Content.Kind)
            .Descending(post => post.Published!.PublishedAt).Descending(post => post.Id),
            new CreateIndexOptions { Name = "ix_posts_public_feed" }),
        new CreateIndexModel<PostDocument>(Builders<PostDocument>.IndexKeys.Ascending(post => post.SchemaVersion)
            .Ascending(post => post.Draft.Language).Ascending(post => post.Draft.Kind)
            .Descending(post => post.UpdatedAt).Descending(post => post.Id),
            new CreateIndexOptions { Name = "ix_posts_admin_feed" })
    ], ct);

    public Task CreateAsync(ContentPost post, CancellationToken ct) => posts.InsertOneAsync(PostDocument.From(post), cancellationToken: ct);

    public async Task<ContentPost?> FindAsync(Guid id, CancellationToken ct) =>
        (await posts.Find(Supported & Builders<PostDocument>.Filter.Eq(post => post.Id, id.ToString("D")))
            .FirstOrDefaultAsync(ct))?.ToDomain();

    public async Task<IReadOnlyList<ContentPost>> ListAsync(bool publishedOnly, string? kind, string? language, int limit, int offset,
        CancellationToken ct, PostFeedFilter? feedFilter = null)
    {
        var filter = Supported;
        var f = Builders<PostDocument>.Filter;
        if (publishedOnly)
        {
            filter &= f.Ne(post => post.Published, null);
            if (kind is not null) filter &= f.Eq(post => post.Published!.Content.Kind, kind);
            if (language is not null) filter &= f.Eq(post => post.Published!.Content.Language, language);
            if (feedFilter?.GameVersion is { } version) filter &= f.Eq(post => post.Published!.Content.GameVersion, version);
            if (feedFilter?.From is { } from) filter &= f.Gte(post => post.Published!.PublishedAt, from.UtcDateTime);
            if (feedFilter?.ToExclusive is { } to) filter &= f.Lt(post => post.Published!.PublishedAt, to.UtcDateTime);
            if (!string.IsNullOrEmpty(feedFilter?.Query))
            {
                var search = new BsonRegularExpression(Regex.Escape(feedFilter.Query), "i");
                filter &= f.Or(f.Regex(post => post.Published!.Content.Title, search),
                    f.Regex(post => post.Published!.Content.Summary, search), f.Regex(post => post.Published!.Content.Body, search));
            }
        }
        else
        {
            if (kind is not null) filter &= f.Eq(post => post.Draft.Kind, kind);
            if (language is not null) filter &= f.Eq(post => post.Draft.Language, language);
        }
        var sort = !publishedOnly ? Builders<PostDocument>.Sort.Descending(post => post.UpdatedAt).Descending(post => post.Id)
            : feedFilter?.OldestFirst == true
                ? Builders<PostDocument>.Sort.Ascending(post => post.Published!.PublishedAt).Ascending(post => post.Id)
                : Builders<PostDocument>.Sort.Descending(post => post.Published!.PublishedAt).Descending(post => post.Id);
        var documents = await posts.Find(filter).Sort(sort).Skip(offset).Limit(limit).ToListAsync(ct);
        return documents.Select(post => post.ToDomain()).ToArray();
    }

    public async Task<bool> TryReplaceAsync(long expectedRevision, ContentPost replacement, CancellationToken ct)
    {
        var result = await posts.ReplaceOneAsync(Supported & Builders<PostDocument>.Filter.Eq(post => post.Id, replacement.Id.ToString("D")) &
            Builders<PostDocument>.Filter.Eq(post => post.Revision, expectedRevision), PostDocument.From(replacement), cancellationToken: ct);
        return result.ModifiedCount == 1;
    }

    public async Task<bool> TryDeleteAsync(Guid id, long expectedRevision, CancellationToken ct)
    {
        var result = await posts.DeleteOneAsync(Supported & Builders<PostDocument>.Filter.Eq(post => post.Id, id.ToString("D")) &
            Builders<PostDocument>.Filter.Eq(post => post.Revision, expectedRevision), ct);
        return result.DeletedCount == 1;
    }
}
