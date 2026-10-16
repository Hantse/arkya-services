using Arkya.Content.Api.Application;
using Arkya.Content.Api.Domain;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Arkya.Content.Api.Infrastructure.Persistence;

public sealed class MongoContentRepository(IMongoClient client, IOptions<MongoOptions> options) : IContentRepository
{
    private readonly IMongoCollection<ContentRevisionDocument> revisions = client.GetDatabase(options.Value.DatabaseName)
        .GetCollection<ContentRevisionDocument>("content_revisions");

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await revisions.Indexes.CreateManyAsync([
            new CreateIndexModel<ContentRevisionDocument>(Builders<ContentRevisionDocument>.IndexKeys.Ascending(document => document.CatalogVersion),
                new CreateIndexOptions { Name = "ux_content_catalog_version", Unique = true }),
            new CreateIndexModel<ContentRevisionDocument>(Builders<ContentRevisionDocument>.IndexKeys
                .Ascending(document => document.PublishedAt).Descending(document => document.Revision),
                new CreateIndexOptions { Name = "ix_content_publication_history" })
        ], cancellationToken);
        try
        {
            await revisions.InsertOneAsync(new ContentRevisionDocument { Revision = 0 }, cancellationToken: cancellationToken);
        }
        catch (MongoWriteException exception) when (exception.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            // Multiple service instances may initialize the same database.
        }
    }

    public async Task<QuestDraft> GetDraftAsync(CancellationToken cancellationToken) =>
        (await LatestAsync(cancellationToken)).ToDraft();

    public async Task<QuestDraft?> TrySaveDraftAsync(long expectedRevision, IReadOnlyList<QuestDefinition> quests,
        CancellationToken cancellationToken)
    {
        if (expectedRevision < 0 || expectedRevision == long.MaxValue ||
            (await LatestAsync(cancellationToken)).Revision != expectedRevision)
            return null;
        var document = new ContentRevisionDocument
        {
            Revision = expectedRevision + 1,
            Quests = quests.Select(QuestDocument.From).ToArray()
        };
        try
        {
            // The next numeric ID is the compare-and-swap: only one editor can insert it.
            await revisions.InsertOneAsync(document, cancellationToken: cancellationToken);
            return document.ToDraft();
        }
        catch (MongoWriteException exception) when (exception.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            return null;
        }
    }

    public async Task<PublicationResult> PublishAsync(long expectedRevision, DateTimeOffset publishedAt,
        CancellationToken cancellationToken)
    {
        var revision = await revisions.Find(document => document.Revision == expectedRevision).FirstOrDefaultAsync(cancellationToken);
        if (revision is null)
            return new(PublicationStatus.Conflict);
        if (revision.PublishedAt is not null)
            return new(PublicationStatus.Published, revision.ToCatalog());
        if (revision.Quests.Length == 0 || (await LatestAsync(cancellationToken)).Revision != expectedRevision)
            return new(PublicationStatus.Conflict);

        // Quest data never changes after insertion. An overlapping edit may advance the draft,
        // while publication atomically exposes exactly the revision observed by this call.
        var published = await revisions.FindOneAndUpdateAsync(
            Builders<ContentRevisionDocument>.Filter.Eq(document => document.Revision, expectedRevision) &
            Builders<ContentRevisionDocument>.Filter.Eq(document => document.PublishedAt, null),
            Builders<ContentRevisionDocument>.Update.Set(document => document.PublishedAt, publishedAt.UtcDateTime),
            new FindOneAndUpdateOptions<ContentRevisionDocument> { ReturnDocument = ReturnDocument.After }, cancellationToken);
        published ??= await revisions.Find(document => document.Revision == expectedRevision).FirstAsync(cancellationToken);
        return new(PublicationStatus.Published, published.ToCatalog());
    }

    public async Task<PublishedCatalog?> FindCatalogAsync(Guid version, CancellationToken cancellationToken)
    {
        var document = await revisions.Find(document => document.CatalogVersion == version.ToString("D") && document.PublishedAt != null)
            .FirstOrDefaultAsync(cancellationToken);
        return document?.ToCatalog();
    }

    public async Task<IReadOnlyList<PublishedCatalog>> ListCatalogsAsync(CancellationToken cancellationToken)
    {
        var documents = await revisions.Find(document => document.PublishedAt != null)
            .SortByDescending(document => document.Revision).ToListAsync(cancellationToken);
        return documents.Select(document => document.ToCatalog()).ToArray();
    }

    private Task<ContentRevisionDocument> LatestAsync(CancellationToken cancellationToken) => revisions
        .Find(FilterDefinition<ContentRevisionDocument>.Empty).SortByDescending(document => document.Revision).FirstAsync(cancellationToken);
}
