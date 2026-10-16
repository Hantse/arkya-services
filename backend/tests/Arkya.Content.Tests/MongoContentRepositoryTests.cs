using Arkya.Content.Api.Application;
using Arkya.Content.Api.Domain;
using Arkya.Content.Api.Infrastructure.Persistence;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Arkya.Content.Tests;

public sealed class MongoContentRepositoryTests
{
    [MongoFact]
    public async Task MongoRevisionIdsProvideAtomicConcurrentEditingAcrossServiceInstances()
    {
        var databaseName = $"arkya_content_test_{Guid.NewGuid():N}";
        var client = Client();
        try
        {
            var first = Repository(client, databaseName);
            var second = Repository(client, databaseName);
            await Task.WhenAll(first.InitializeAsync(CancellationToken.None), second.InitializeAsync(CancellationToken.None));
            var results = await Task.WhenAll(
                first.TrySaveDraftAsync(0, [ContentFixtures.Definition(3)], CancellationToken.None),
                second.TrySaveDraftAsync(0, [ContentFixtures.Definition(7)], CancellationToken.None));
            Assert.Single(results, result => result is not null);
            Assert.Single(results, result => result is null);
            Assert.Equal(1, (await second.GetDraftAsync(CancellationToken.None)).Revision);
            Assert.Equal(PublicationStatus.Conflict, (await second.PublishAsync(0, FixedTimeProvider.Now, CancellationToken.None)).Status);
            using var indexesCursor = await client.GetDatabase(databaseName).GetCollection<BsonDocument>("content_revisions").Indexes.ListAsync();
            var indexes = await indexesCursor.ToListAsync();
            Assert.True(indexes.Single(index => index["name"] == "ux_content_catalog_version")["unique"].AsBoolean);
            Assert.Contains(indexes, index => index["name"] == "ix_content_publication_history");
        }
        finally { await client.DropDatabaseAsync(databaseName); }
    }

    [MongoFact]
    public async Task MongoPublicationRetriesAndRestartPreserveImmutableCatalogAndRejectStaleUnpublishedDraft()
    {
        var databaseName = $"arkya_content_test_{Guid.NewGuid():N}";
        var client = Client();
        try
        {
            var repository = Repository(client, databaseName);
            await repository.InitializeAsync(CancellationToken.None);
            await repository.TrySaveDraftAsync(0, [ContentFixtures.Definition(3)], CancellationToken.None);
            var publications = await Task.WhenAll(Enumerable.Range(0, 8)
                .Select(index => Repository(client, databaseName).PublishAsync(1, FixedTimeProvider.Now.AddSeconds(index), CancellationToken.None)));
            Assert.All(publications, result => Assert.Equal(PublicationStatus.Published, result.Status));
            var original = publications[0].Catalog!;
            Assert.Single(publications.Select(result => result.Catalog!.Version).Distinct());
            Assert.Single(publications.Select(result => result.Catalog!.PublishedAt).Distinct());
            await repository.TrySaveDraftAsync(1, [ContentFixtures.Definition(7)], CancellationToken.None);
            await repository.TrySaveDraftAsync(2, [ContentFixtures.Definition(9)], CancellationToken.None);
            Assert.Equal(PublicationStatus.Conflict,
                (await repository.PublishAsync(2, FixedTimeProvider.Now, CancellationToken.None)).Status);
            var restarted = Repository(client, databaseName);
            await restarted.InitializeAsync(CancellationToken.None);
            var retry = (await restarted.PublishAsync(1, FixedTimeProvider.Now.AddDays(1), CancellationToken.None)).Catalog!;
            AssertCatalog(original, retry);
            AssertCatalog(original, (await restarted.FindCatalogAsync(original.Version, CancellationToken.None))!);
            AssertCatalog(original, Assert.Single(await restarted.ListCatalogsAsync(CancellationToken.None)));
            var documents = await client.GetDatabase(databaseName).GetCollection<BsonDocument>("content_revisions")
                .Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
            Assert.Equal(4, documents.Count);
            Assert.All(documents, document => Assert.Equal(1, document["SchemaVersion"].AsInt32));
        }
        finally { await client.DropDatabaseAsync(databaseName); }
    }

    private static void AssertCatalog(PublishedCatalog expected, PublishedCatalog actual)
    {
        Assert.Equal(expected.Version, actual.Version);
        Assert.Equal(expected.PublishedAt, actual.PublishedAt);
        Assert.Equal(expected.SchemaVersion, actual.SchemaVersion);
        Assert.Equal(expected.Quests, actual.Quests);
    }

    private static MongoContentRepository Repository(IMongoClient client, string databaseName) =>
        new(client, Options.Create(new MongoOptions { DatabaseName = databaseName }));

    private static MongoClient Client()
    {
        var settings = MongoClientSettings.FromConnectionString(Environment.GetEnvironmentVariable("ARKYA_TEST_MONGO"));
        settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
        return new MongoClient(settings);
    }
}

public sealed class MongoFactAttribute : FactAttribute
{
    public MongoFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ARKYA_TEST_MONGO")))
            Skip = "Set ARKYA_TEST_MONGO to run against a disposable MongoDB database.";
    }
}
