using System.Text.RegularExpressions;
using Arkya.Content.Api.Application;
using Arkya.Content.Api.Domain;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Arkya.Content.Api.Infrastructure.Persistence;

public sealed class MongoItemContentRepository(IMongoClient client, IOptions<MongoOptions> options) : IItemContentRepository
{
    private readonly IMongoCollection<ItemRevisionDocument> revisions = client.GetDatabase(options.Value.DatabaseName)
        .GetCollection<ItemRevisionDocument>("item_revisions");
    private readonly IMongoCollection<ItemVisualAssetDocument> assets = client.GetDatabase(options.Value.DatabaseName)
        .GetCollection<ItemVisualAssetDocument>("item_visual_assets");

    public async Task InitializeAsync(CancellationToken ct)
    {
        await revisions.Indexes.CreateManyAsync([
            new CreateIndexModel<ItemRevisionDocument>(Builders<ItemRevisionDocument>.IndexKeys.Ascending(document => document.CatalogVersion),
                new CreateIndexOptions { Name = "ux_item_catalog_version", Unique = true }),
            new CreateIndexModel<ItemRevisionDocument>(Builders<ItemRevisionDocument>.IndexKeys.Descending(document => document.PublishedAt)
                .Descending(document => document.Revision), new CreateIndexOptions { Name = "ix_item_publication_history" })
        ], ct);
        await assets.Indexes.CreateOneAsync(new CreateIndexModel<ItemVisualAssetDocument>(
            Builders<ItemVisualAssetDocument>.IndexKeys.Ascending(document => document.Name).Ascending(document => document.Guid),
            new CreateIndexOptions { Name = "ix_item_visual_asset_name" }), cancellationToken: ct);
        try { await revisions.InsertOneAsync(new ItemRevisionDocument { Revision = 0 }, cancellationToken: ct); }
        catch (MongoWriteException exception) when (exception.WriteError.Category == ServerErrorCategory.DuplicateKey) { }
    }

    public async Task<ItemDraft> GetDraftAsync(CancellationToken ct) => (await LatestAsync(ct)).ToDraft();

    public async Task<ItemDraft?> TrySaveDraftAsync(long expectedRevision, IReadOnlyList<ItemDefinition> items, CancellationToken ct)
    {
        if (expectedRevision < 0 || expectedRevision == long.MaxValue || (await LatestAsync(ct)).Revision != expectedRevision) return null;
        var document = new ItemRevisionDocument { Revision = expectedRevision + 1, Items = items.Select(ItemDocument.From).ToArray() };
        try
        {
            // The next numeric ID makes concurrent saves a single-winner operation across service instances.
            await revisions.InsertOneAsync(document, cancellationToken: ct);
            return document.ToDraft();
        }
        catch (MongoWriteException exception) when (exception.WriteError.Category == ServerErrorCategory.DuplicateKey) { return null; }
    }

    public async Task<ItemCatalog?> FindPublishedRevisionAsync(long revision, CancellationToken ct) =>
        (await revisions.Find(document => document.Revision == revision && document.PublishedAt != null).FirstOrDefaultAsync(ct))?.ToCatalog();

    public async Task<ItemPublicationResult> PublishAsync(long expectedRevision, IReadOnlyList<PublishedItem> items,
        DateTimeOffset publishedAt, CancellationToken ct)
    {
        var revision = await revisions.Find(document => document.Revision == expectedRevision).FirstOrDefaultAsync(ct);
        if (revision is null) return new(false);
        if (revision.PublishedAt is not null) return new(true, revision.ToCatalog());
        if (revision.Items.Length == 0 || (await LatestAsync(ct)).Revision != expectedRevision) return new(false);
        var published = await revisions.FindOneAndUpdateAsync(
            Builders<ItemRevisionDocument>.Filter.Eq(document => document.Revision, expectedRevision) &
            Builders<ItemRevisionDocument>.Filter.Eq(document => document.PublishedAt, null),
            Builders<ItemRevisionDocument>.Update.Set(document => document.PublishedAt, publishedAt.UtcDateTime)
                .Set(document => document.PublishedItems, items.Select(PublishedItemDocument.From).ToArray()),
            new FindOneAndUpdateOptions<ItemRevisionDocument> { ReturnDocument = ReturnDocument.After }, ct);
        published ??= await revisions.Find(document => document.Revision == expectedRevision).FirstAsync(ct);
        return new(true, published.ToCatalog());
    }

    public async Task<ItemCatalog?> FindCatalogAsync(Guid version, CancellationToken ct) =>
        (await revisions.Find(document => document.CatalogVersion == version.ToString("D") && document.PublishedAt != null)
            .FirstOrDefaultAsync(ct))?.ToCatalog();

    public async Task<IReadOnlyList<ItemCatalog>> ListCatalogsAsync(int limit, CancellationToken ct) =>
        (await revisions.Find(document => document.PublishedAt != null).SortByDescending(document => document.Revision)
            .Limit(limit).ToListAsync(ct)).Select(document => document.ToCatalog()).ToArray();

    public async Task<ItemVisualAsset?> FindAssetAsync(string guid, CancellationToken ct) =>
        (await assets.Find(document => document.Guid == guid).FirstOrDefaultAsync(ct))?.ToDomain();

    public async Task<IReadOnlyList<ItemVisualAsset>> FindAssetsAsync(IEnumerable<string> guids, CancellationToken ct) =>
        (await assets.Find(Builders<ItemVisualAssetDocument>.Filter.In(document => document.Guid, guids))
            .ToListAsync(ct)).Select(document => document.ToDomain()).ToArray();

    public async Task<IReadOnlyList<ItemVisualAsset>> ListAssetsAsync(string? search, int limit, int offset, CancellationToken ct)
    {
        var filter = Builders<ItemVisualAssetDocument>.Filter.Empty;
        if (!string.IsNullOrEmpty(search))
        {
            var pattern = new BsonRegularExpression(Regex.Escape(search), "i");
            filter = Builders<ItemVisualAssetDocument>.Filter.Regex(document => document.Name, pattern) |
                Builders<ItemVisualAssetDocument>.Filter.Regex(document => document.AssetPath, pattern);
        }
        var documents = await assets.Find(filter).SortBy(document => document.Name).ThenBy(document => document.Guid)
            .Skip(offset).Limit(limit).Project<ItemVisualAssetDocument>(Builders<ItemVisualAssetDocument>.Projection.Exclude(document => document.ThumbnailPng))
            .ToListAsync(ct);
        // Thumbnail availability is returned separately without transferring binary data in list responses.
        var guids = documents.Select(document => document.Guid).ToArray();
        var withThumbnail = await assets.Find(Builders<ItemVisualAssetDocument>.Filter.In(document => document.Guid, guids) &
            Builders<ItemVisualAssetDocument>.Filter.Ne(document => document.ThumbnailPng, null))
            .Project(document => document.Guid).ToListAsync(ct);
        var thumbnailGuids = withThumbnail.ToHashSet(StringComparer.Ordinal);
        return documents.Select(document => document.ToDomain() with { ThumbnailPng = thumbnailGuids.Contains(document.Guid) ? [] : null }).ToArray();
    }

    public Task UpsertAssetAsync(ItemVisualAsset asset, CancellationToken ct) => assets.ReplaceOneAsync(
        document => document.Guid == asset.Guid, ItemVisualAssetDocument.From(asset), new ReplaceOptions { IsUpsert = true }, ct);
    public async Task<bool> DeleteAssetAsync(string guid, CancellationToken ct) =>
        (await assets.DeleteOneAsync(document => document.Guid == guid, ct)).DeletedCount == 1;

    private Task<ItemRevisionDocument> LatestAsync(CancellationToken ct) => revisions.Find(Builders<ItemRevisionDocument>.Filter.Empty)
        .SortByDescending(document => document.Revision).FirstAsync(ct);
}
