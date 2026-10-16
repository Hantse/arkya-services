using Arkya.Characters.Api.Application;
using Arkya.Characters.Api.Domain;
using Arkya.Characters.Api.Infrastructure.Persistence;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Arkya.Characters.Tests;

public sealed class MongoCharacterRepositoryTests
{
    [MongoFact]
    public async Task MongoUniqueIndexMakesConcurrentNameReservationAtomicAcrossAccounts()
    {
        var databaseName = $"arkya_characters_test_{Guid.NewGuid():N}";
        var client = CreateClient();
        try
        {
            var repository = new MongoCharacterRepository(client, Options.Create(new MongoOptions { DatabaseName = databaseName }));
            await repository.EnsureIndexesAsync(CancellationToken.None);
            using var indexCursor = await client.GetDatabase(databaseName).GetCollection<BsonDocument>("characters").Indexes.ListAsync();
            var indexes = await indexCursor.ToListAsync();
            Assert.True(indexes.Single(index => index["name"] == "ux_character_normalized_name")["unique"].AsBoolean);
            Assert.Contains(indexes, index => index["name"] == "ix_character_owner_created");
            var service = new CharacterService(repository, new FixedTimeProvider());

            var results = await Task.WhenAll(
                service.CreateAsync(Guid.NewGuid(), "Aeria", CancellationToken.None),
                service.CreateAsync(Guid.NewGuid(), " aeria ", CancellationToken.None));

            Assert.Single(results, result => result.Status == CreateCharacterStatus.Created);
            Assert.Single(results, result => result.Status == CreateCharacterStatus.NameTaken);
            var documents = await client.GetDatabase(databaseName).GetCollection<BsonDocument>("characters")
                .Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
            var document = Assert.Single(documents);
            Assert.Equal(2, document["SchemaVersion"].AsInt32);
            Assert.True(Guid.TryParse(document["_id"].AsString, out _));
            Assert.True(Guid.TryParse(document["AccountId"].AsString, out _));
            Assert.Equal(1, document["Level"].AsInt32);
        }
        finally
        {
            await client.DropDatabaseAsync(databaseName);
        }
    }

    [MongoFact]
    public async Task MongoReadsAlwaysFilterTheAccountAndPreserveTheStoredCharacter()
    {
        var databaseName = $"arkya_characters_test_{Guid.NewGuid():N}";
        var client = CreateClient();
        try
        {
            var repository = new MongoCharacterRepository(client, Options.Create(new MongoOptions { DatabaseName = databaseName }));
            await repository.EnsureIndexesAsync(CancellationToken.None);
            var service = new CharacterService(repository, new FixedTimeProvider());
            var ownerId = Guid.NewGuid();
            var strangerId = Guid.NewGuid();
            var appearance = new CharacterAppearance(1, "elf", "b", 202, "ebony", "braid", "silver", "violet", "wine");
            var created = (await service.CreateAsync(ownerId, "Aeria", appearance, CancellationToken.None)).Character!;

            Assert.Equal(created, await repository.FindOwnedAsync(created.Id, ownerId, CancellationToken.None));
            Assert.Null(await repository.FindOwnedAsync(created.Id, strangerId, CancellationToken.None));
            Assert.Equal(created, Assert.Single(await repository.ListOwnedAsync(ownerId, CancellationToken.None)));
            Assert.Empty(await repository.ListOwnedAsync(strangerId, CancellationToken.None));
        }
        finally
        {
            await client.DropDatabaseAsync(databaseName);
        }
    }

    [MongoFact]
    public async Task LegacyMongoDocumentsReadWithDefaultAppearanceAndRetainOwnership()
    {
        var databaseName = $"arkya_characters_test_{Guid.NewGuid():N}";
        var client = CreateClient();
        try
        {
            var repository = new MongoCharacterRepository(client, Options.Create(new MongoOptions { DatabaseName = databaseName }));
            var original = new Character(Guid.NewGuid(), Guid.NewGuid(), "Aeria", "AERIA", 1, FixedTimeProvider.Now);
            var legacy = CharacterDocument.From(original).ToBsonDocument();
            legacy["SchemaVersion"] = 1;
            legacy.Remove("Appearance");
            await client.GetDatabase(databaseName).GetCollection<BsonDocument>("characters").InsertOneAsync(legacy);

            Assert.Equal(original, await repository.FindOwnedAsync(original.Id, original.AccountId, CancellationToken.None));
            Assert.Equal(original, Assert.Single(await repository.ListOwnedAsync(original.AccountId, CancellationToken.None)));
            Assert.Null(await repository.FindOwnedAsync(original.Id, Guid.NewGuid(), CancellationToken.None));
        }
        finally
        {
            await client.DropDatabaseAsync(databaseName);
        }
    }

    private static MongoClient CreateClient()
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
