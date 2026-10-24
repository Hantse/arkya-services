using Arkya.Content.Api.Application;
using Arkya.Content.Api.Contracts;
using Arkya.Content.Api.Domain;
using Arkya.Content.Api.Infrastructure.Persistence;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Arkya.Content.Tests;

public sealed class MongoPostRepositoryTests
{
    [MongoFact]
    public async Task AtomicRevisionGuardsWorkAcrossInstancesAndPublishedSnapshotSurvivesRestartAndDraftChanges()
    {
        var client = new MongoClient(Environment.GetEnvironmentVariable("ARKYA_TEST_MONGO"));
        var database = $"arkya_posts_test_{Guid.NewGuid():N}";
        try
        {
            var repository = Repository(client, database);
            var other = Repository(client, database);
            await Task.WhenAll(repository.InitializeAsync(default), other.InitializeAsync(default));
            var service = new PostService(repository, new FixedTimeProvider());
            var created = (await service.CreateAsync(PostEndpointTests.News(), default)).Post!;
            var updates = await Task.WhenAll(Enumerable.Range(0, 8).Select(index =>
                new PostService(Repository(client, database), new FixedTimeProvider()).UpdateAsync(created.Id,
                    new UpdatePostRequest(1, "news", "fr", $"Published {index}", "", "Original body", null), default)));
            Assert.Single(updates, result => result.Status == PostChangeStatus.Saved);
            Assert.Equal(7, updates.Count(result => result.Status == PostChangeStatus.Conflict));
            var published = (await service.PublishAsync(created.Id, 2, true, default)).Post!;
            Assert.Equal(3, published.Revision);
            Assert.Equal(3, published.Published!.Revision);
            await service.UpdateAsync(created.Id, new(3, "release-note", "en", "Private edit", "", "Private body", "1.2.3"), default);
            var restarted = Repository(client, database);
            await restarted.InitializeAsync(default);
            var saved = (await restarted.FindAsync(created.Id, default))!;
            Assert.Equal(4, saved.Revision);
            Assert.Equal("Private edit", saved.Draft.Title);
            Assert.Equal(published.Published, saved.Published);
            Assert.Single(await restarted.ListAsync(true, "news", "fr", 20, 0, default));
            Assert.Empty(await restarted.ListAsync(true, "release-note", "en", 20, 0, default));
            Assert.Single(await restarted.ListAsync(false, "release-note", "en", 20, 0, default));

            var commands = await Task.WhenAll(new PostService(other, new FixedTimeProvider()).PublishAsync(created.Id, 4, true, default),
                service.PublishAsync(created.Id, 4, false, default));
            Assert.Single(commands, result => result.Status == PostChangeStatus.Saved);
            Assert.Single(commands, result => result.Status == PostChangeStatus.Conflict);
            Assert.False(await restarted.TryDeleteAsync(created.Id, 4, default));
            Assert.True(await restarted.TryDeleteAsync(created.Id, 5, default));
            Assert.Null(await restarted.FindAsync(created.Id, default));
        }
        finally { await client.DropDatabaseAsync(database); }
    }

    [MongoFact]
    public async Task FeedIndexesPaginationAndSchemaIsolationArePersistedWithoutSeedingArticles()
    {
        var client = new MongoClient(Environment.GetEnvironmentVariable("ARKYA_TEST_MONGO"));
        var database = $"arkya_posts_test_{Guid.NewGuid():N}";
        try
        {
            var repository = Repository(client, database);
            await repository.InitializeAsync(default);
            var documents = client.GetDatabase(database).GetCollection<BsonDocument>("content_posts");
            Assert.Equal(0, await documents.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));
            var service = new PostService(repository, new FixedTimeProvider());
            for (var i = 0; i < 3; i++)
            {
                var post = (await service.CreateAsync(PostEndpointTests.News($"News {i}"), default)).Post!;
                await service.PublishAsync(post.Id, 1, true, default);
            }
            var all = await repository.ListAsync(true, null, null, 10, 0, default);
            Assert.Equal(all.Select(post => post.Id.ToString("D")).OrderDescending(StringComparer.Ordinal), all.Select(post => post.Id.ToString("D")));
            Assert.Equal(all.Skip(1).Take(1), await repository.ListAsync(true, null, null, 1, 1, default));
            var unknown = (await documents.Find(FilterDefinition<BsonDocument>.Empty).FirstAsync()).DeepClone().AsBsonDocument;
            var unknownId = Guid.NewGuid();
            unknown["_id"] = unknownId.ToString("D");
            unknown["SchemaVersion"] = 99;
            await documents.InsertOneAsync(unknown);
            Assert.Null(await repository.FindAsync(unknownId, default));
            Assert.Equal(3, (await repository.ListAsync(true, null, null, 10, 0, default)).Count);
            Assert.False(await repository.TryReplaceAsync(2, all[0] with { Id = unknownId, Revision = 3 }, default));
            Assert.False(await repository.TryDeleteAsync(unknownId, 2, default));
            using var cursor = await documents.Indexes.ListAsync();
            var names = (await cursor.ToListAsync()).Select(index => index["name"].AsString).ToArray();
            Assert.Contains("ix_posts_public_feed", names);
            Assert.Contains("ix_posts_admin_feed", names);
        }
        finally { await client.DropDatabaseAsync(database); }
    }

    [MongoFact]
    public async Task PublicFiltersApplyToPublishedSnapshotBeforePaginationWithStableSortAndLiteralSearch()
    {
        var client = new MongoClient(Environment.GetEnvironmentVariable("ARKYA_TEST_MONGO"));
        var database = $"arkya_posts_test_{Guid.NewGuid():N}";
        try
        {
            var repository = Repository(client, database);
            await repository.InitializeAsync(default);
            await PostFeedFixtures.SeedAsync(repository);
            var from = new DateTimeOffset(2026, 10, 23, 0, 0, 0, TimeSpan.Zero);
            var filter = new PostFeedFilter("RECRUTEMENT", "0.4-beta", from, from.AddDays(1));
            var first = await repository.ListAsync(true, "release-note", "fr", 2, 0, default, filter);
            var second = await repository.ListAsync(true, "release-note", "fr", 2, 2, default, filter);
            Assert.Equal(new[] { PostFeedFixtures.Id(3), PostFeedFixtures.Id(10) }, first.Select(post => post.Id));
            Assert.Equal(new[] { PostFeedFixtures.Id(2), PostFeedFixtures.Id(1) }, second.Select(post => post.Id));
            Assert.Empty(await repository.ListAsync(true, "release-note", "fr", 2, 4, default, filter));
            var oldest = await repository.ListAsync(true, "release-note", "fr", 2, 0, default, filter with { OldestFirst = true });
            Assert.Equal(new[] { PostFeedFixtures.Id(1), PostFeedFixtures.Id(2) }, oldest.Select(post => post.Id));
            var literal = await repository.ListAsync(true, null, null, 20, 0, default, new("literal.*[text]"));
            Assert.Equal(PostFeedFixtures.Id(10), Assert.Single(literal).Id);
            Assert.Empty(await repository.ListAsync(true, null, null, 20, 0, default, new("private draft")));
            Assert.Empty(await repository.ListAsync(true, null, null, 20, 0, default, new(GameVersion: "9.9-private")));
            Assert.Empty(await repository.ListAsync(true, null, null, 20, 0, default, new(GameVersion: "0.4-BETA")));
            var service = new PostService(repository, new FixedTimeProvider());
            Assert.Equal(3, (await service.ListAsync(true, "release-note", "fr", 2, 0, default, filter)).Count);
            Assert.Equal(2, (await service.ListAsync(true, "release-note", "fr", 2, 2, default, filter)).Count);
        }
        finally { await client.DropDatabaseAsync(database); }
    }

    [MongoFact]
    public async Task AdminFiltersUseDraftAndUpdatedTimeBeforePagingIncludingUnpublishedPosts()
    {
        var client = new MongoClient(Environment.GetEnvironmentVariable("ARKYA_TEST_MONGO"));
        var database = $"arkya_posts_test_{Guid.NewGuid():N}";
        try
        {
            var repository = Repository(client, database);
            await repository.InitializeAsync(default);
            await AdminPostFeedFixtures.SeedAsync(repository);
            var from = new DateTimeOffset(2026, 10, 23, 0, 0, 0, TimeSpan.Zero);
            var filter = new PostFeedFilter("RECRUTEMENT", "0.4-beta", from, from.AddDays(1));
            var first = await repository.ListAsync(false, "release-note", "fr", 2, 0, default, filter);
            var second = await repository.ListAsync(false, "release-note", "fr", 2, 2, default, filter);
            var third = await repository.ListAsync(false, "release-note", "fr", 2, 4, default, filter);
            Assert.Equal(new[] { AdminPostFeedFixtures.Id(3), AdminPostFeedFixtures.Id(10) }, first.Select(post => post.Id));
            Assert.Equal(new[] { AdminPostFeedFixtures.Id(9), AdminPostFeedFixtures.Id(2) }, second.Select(post => post.Id));
            Assert.Null(second[0].Published);
            Assert.Equal(AdminPostFeedFixtures.Id(1), Assert.Single(third).Id);
            var oldest = await repository.ListAsync(false, "release-note", "fr", 2, 0, default, filter with { OldestFirst = true });
            Assert.Equal(new[] { AdminPostFeedFixtures.Id(1), AdminPostFeedFixtures.Id(2) }, oldest.Select(post => post.Id));
            var literal = await repository.ListAsync(false, null, null, 20, 0, default, new("literal.*[text]"));
            Assert.Equal(AdminPostFeedFixtures.Id(10), Assert.Single(literal).Id);
            Assert.Empty(await repository.ListAsync(false, null, null, 20, 0, default, new("PublishedOnlyMarker")));
            Assert.Empty(await repository.ListAsync(false, null, null, 20, 0, default, new(GameVersion: "0.4-BETA")));
            var dateOnly = await repository.ListAsync(false, null, null, 20, 0, default, new(From: from, ToExclusive: from.AddDays(1)));
            Assert.Equal(9, dateOnly.Count);
            Assert.All(dateOnly, post => Assert.Equal(new DateOnly(2026, 10, 23), DateOnly.FromDateTime(post.UpdatedAt.UtcDateTime)));
            var service = new PostService(repository, new FixedTimeProvider());
            Assert.Equal(3, (await service.ListAsync(false, "release-note", "fr", 2, 0, default, filter)).Count);
            Assert.Single(await service.ListAsync(false, "release-note", "fr", 2, 4, default, filter));
        }
        finally { await client.DropDatabaseAsync(database); }
    }

    private static MongoPostRepository Repository(IMongoClient client, string database) =>
        new(client, Options.Create(new MongoOptions { DatabaseName = database }));
}
