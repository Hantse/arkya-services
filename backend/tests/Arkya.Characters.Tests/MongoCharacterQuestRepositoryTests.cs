using Arkya.Characters.Api.Application;
using Arkya.Characters.Api.Infrastructure.Persistence;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Arkya.Characters.Tests;

public sealed class MongoCharacterQuestRepositoryTests
{
    [MongoFact]
    public async Task MongoAcceptanceAndCompareAndSwapAreAtomicAndPersistAfterRepositoryRecreation()
    {
        var databaseName = $"arkya_character_quests_test_{Guid.NewGuid():N}";
        var settings = MongoClientSettings.FromConnectionString(Environment.GetEnvironmentVariable("ARKYA_TEST_MONGO"));
        settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
        var client = new MongoClient(settings);
        var options = Options.Create(new MongoOptions { DatabaseName = databaseName });
        try
        {
            var characters = new MongoCharacterRepository(client, options);
            var quests = new MongoCharacterQuestRepository(client, options);
            await characters.EnsureIndexesAsync(CancellationToken.None);
            await quests.EnsureIndexesAsync(CancellationToken.None);
            var character = (await new CharacterService(characters, new FixedTimeProvider())
                .CreateAsync(Guid.NewGuid(), "Aeria", CancellationToken.None)).Character!;
            var firstServer = new CharacterQuestService(characters, quests);
            var secondServer = new CharacterQuestService(characters, new MongoCharacterQuestRepository(client, options));
            var version = Guid.NewGuid().ToString("D");
            Task<SaveCharacterQuestResult> Save(CharacterQuestService service, int kills, long revision) =>
                service.SaveAsync(character.Id, "hunt-wolves", version, 3, kills, revision, CancellationToken.None);

            var accepts = await Task.WhenAll(Save(firstServer, 0, 0), Save(secondServer, 0, 0));
            Assert.All(accepts, result => Assert.Equal(SaveCharacterQuestStatus.Saved, result.Status));
            Assert.All(accepts, result => Assert.Equal(1, result.Quest!.Revision));
            var progresses = await Task.WhenAll(Save(firstServer, 1, 1), Save(secondServer, 2, 1));
            Assert.Single(progresses, result => result.Status == SaveCharacterQuestStatus.Saved);
            Assert.Single(progresses, result => result.Status == SaveCharacterQuestStatus.Conflict);
            var stored = Assert.Single((await secondServer.ListAsync(character.Id, CancellationToken.None))!);
            Assert.Equal(2, stored.Revision);
            Assert.Equal(SaveCharacterQuestStatus.Conflict, (await Save(secondServer, 0, 2)).Status);
            Assert.Equal(SaveCharacterQuestStatus.Saved, (await Save(secondServer, stored.Kills, 1)).Status);
            Assert.Equal(SaveCharacterQuestStatus.Conflict, (await secondServer.SaveAsync(character.Id, "hunt-wolves",
                Guid.NewGuid().ToString("D"), 3, 3, 2, CancellationToken.None)).Status);

            var collection = client.GetDatabase(databaseName).GetCollection<BsonDocument>("character_quests");
            var document = Assert.Single(await collection.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync());
            Assert.Equal(1, document["SchemaVersion"].AsInt32);
            Assert.Equal(character.Id.ToString("D"), document["CharacterId"].AsString);
            Assert.Equal(version, document["CatalogVersion"].AsString);
            Assert.Equal(2, document["Revision"].AsInt64);
            Assert.False(document.Contains("Completed"));
            using var cursor = await collection.Indexes.ListAsync();
            Assert.True((await cursor.ToListAsync()).Single(index => index["name"] == "ux_character_quest")["unique"].AsBoolean);
        }
        finally
        {
            await client.DropDatabaseAsync(databaseName);
        }
    }
}
