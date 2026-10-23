using Arkya.Content.Api.Domain;
using Arkya.Content.Api.Application;
using Arkya.Content.Api.Infrastructure.Persistence;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Arkya.Content.Tests;

public sealed class MongoItemContentRepositoryTests
{
    [MongoFact]
    public async Task DropSourcesPersistAcrossRestartAndLegacyDocumentsDefaultToNoDrops()
    {
        var databaseName = $"arkya_item_drops_test_{Guid.NewGuid():N}";
        var client = new MongoClient(Environment.GetEnvironmentVariable("ARKYA_TEST_MONGO"));
        MongoItemContentRepository Repository() => new(client, Options.Create(new MongoOptions { DatabaseName = databaseName }));
        var ct = CancellationToken.None;
        try
        {
            var repository = Repository();
            await repository.InitializeAsync(ct);
            var item = ItemFixtures.Definition() with { DropSources = [new(ContentService.SupportedEnemyId, 1234)] };
            await repository.TrySaveDraftAsync(0, [item], ct);
            var publication = await repository.PublishAsync(1,
                [new PublishedItem(item, ItemFixtures.Asset().AssetPath, ItemFixtures.Hash)], FixedTimeProvider.Now, ct);
            await repository.TrySaveDraftAsync(1, [item with { DropSources = [] }], ct);

            var restarted = Repository();
            Assert.Empty(Assert.Single((await restarted.GetDraftAsync(ct)).Items).DropSources);
            var frozen = (await restarted.FindCatalogAsync(publication.Catalog!.Version, ct))!;
            Assert.Equal(new ItemDropSource(ContentService.SupportedEnemyId, 1234), Assert.Single(Assert.Single(frozen.Items).Definition.DropSources));

            var documents = client.GetDatabase(databaseName).GetCollection<BsonDocument>("item_revisions");
            await documents.UpdateOneAsync(Builders<BsonDocument>.Filter.Eq("_id", 2L),
                Builders<BsonDocument>.Update.Unset("Items.0.DropSources"), cancellationToken: ct);
            await documents.UpdateOneAsync(Builders<BsonDocument>.Filter.Eq("_id", 1L),
                Builders<BsonDocument>.Update.Unset("PublishedItems.0.Definition.DropSources"), cancellationToken: ct);
            Assert.Empty(Assert.Single((await restarted.GetDraftAsync(ct)).Items).DropSources);
            var legacy = (await restarted.FindCatalogAsync(publication.Catalog.Version, ct))!;
            Assert.Empty(Assert.Single(legacy.Items).Definition.DropSources);
        }
        finally { await client.DropDatabaseAsync(databaseName); }
    }

    [MongoFact]
    public async Task ConcurrentEditingAndPublicationPersistImmutableVisualSnapshotAcrossRestart()
    {
        var databaseName = $"arkya_items_test_{Guid.NewGuid():N}";
        var settings = MongoClientSettings.FromConnectionString(Environment.GetEnvironmentVariable("ARKYA_TEST_MONGO"));
        settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
        var client = new MongoClient(settings);
        MongoItemContentRepository Repository() => new(client, Options.Create(new MongoOptions { DatabaseName = databaseName }));
        var ct = CancellationToken.None;
        try
        {
            var first = Repository(); var second = Repository();
            await Task.WhenAll(first.InitializeAsync(ct), second.InitializeAsync(ct));
            await first.UpsertAssetAsync(ItemFixtures.Asset() with { Name = "Sword [A]" }, ct);
            Assert.True(Assert.Single(await first.ListAssetsAsync("[A]", 10, 0, ct)).ThumbnailPng is not null);
            Assert.Empty(await first.ListAssetsAsync(".*", 10, 0, ct));
            Assert.NotEmpty((await first.FindAssetAsync(ItemFixtures.Guid, ct))!.ThumbnailPng!);
            Assert.Single(await first.FindAssetsAsync([ItemFixtures.Guid], ct));
            var saves = await Task.WhenAll(first.TrySaveDraftAsync(0, [ItemFixtures.Definition()], ct),
                second.TrySaveDraftAsync(0, [ItemFixtures.Definition(17)], ct));
            Assert.Single(saves, draft => draft is not null); Assert.Single(saves, draft => draft is null);
            var draft = await first.GetDraftAsync(ct);
            var snapshot = draft.Items.Select(item => new PublishedItem(item, ItemFixtures.Asset().AssetPath, ItemFixtures.Hash)).ToArray();
            var publications = await Task.WhenAll(Enumerable.Range(0, 8).Select(index => Repository().PublishAsync(1,
                snapshot.Select(item => item with { VisualDependencyHash = new string((char)('a' + index % 6), 32) }).ToArray(),
                FixedTimeProvider.Now.AddSeconds(index), ct)));
            Assert.All(publications, result => Assert.True(result.Published));
            Assert.Single(publications.Select(result => result.Catalog!.Version).Distinct());
            Assert.Single(publications.Select(result => result.Catalog!.PublishedAt).Distinct());
            Assert.Single(publications.Select(result => result.Catalog!.Items[0].VisualDependencyHash).Distinct());
            var original = publications[0].Catalog!;
            await first.UpsertAssetAsync(ItemFixtures.Asset("Assets/Items/NewSword.prefab", new string('c', 32)), ct);
            await first.TrySaveDraftAsync(1, [ItemFixtures.Definition(20)], ct);
            await first.TrySaveDraftAsync(2, [ItemFixtures.Definition(30)], ct);
            Assert.False((await first.PublishAsync(2, snapshot, FixedTimeProvider.Now, ct)).Published);
            Assert.True(await first.DeleteAssetAsync(ItemFixtures.Guid, ct));
            Assert.False(await first.DeleteAssetAsync(ItemFixtures.Guid, ct));
            var restarted = Repository(); await restarted.InitializeAsync(ct);
            var retry = (await restarted.PublishAsync(1, [], FixedTimeProvider.Now.AddDays(1), ct)).Catalog!;
            Assert.Equal(original.Version, retry.Version); Assert.Equal(original.PublishedAt, retry.PublishedAt);
            Assert.Equal(original.Items, retry.Items);
            Assert.Equal(original.Items, (await restarted.FindCatalogAsync(original.Version, ct))!.Items);
            Assert.Equal(original.Version, (await restarted.FindPublishedRevisionAsync(1, ct))!.Version);
            Assert.Equal(original.Version, Assert.Single(await restarted.ListCatalogsAsync(1, ct)).Version);
            Assert.Null(await restarted.FindAssetAsync(ItemFixtures.Guid, ct));
            var db = client.GetDatabase(databaseName);
            using var cursor = await db.GetCollection<BsonDocument>("item_revisions").Indexes.ListAsync();
            var indexes = await cursor.ToListAsync();
            Assert.True(indexes.Single(index => index["name"] == "ux_item_catalog_version")["unique"].AsBoolean);
            Assert.Contains(indexes, index => index["name"] == "ix_item_publication_history");
            var documents = await db.GetCollection<BsonDocument>("item_revisions").Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
            Assert.Equal(4, documents.Count); Assert.All(documents, document => Assert.Equal(1, document["SchemaVersion"].AsInt32));
        }
        finally { await client.DropDatabaseAsync(databaseName); }
    }
}
