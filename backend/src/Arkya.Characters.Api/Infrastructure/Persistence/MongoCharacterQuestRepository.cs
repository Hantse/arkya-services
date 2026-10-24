using Arkya.Characters.Api.Application;
using Arkya.Characters.Api.Domain;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Arkya.Characters.Api.Infrastructure.Persistence;

public sealed class MongoCharacterQuestRepository : ICharacterQuestRepository
{
    private readonly IMongoDatabase legacyDatabase;
    private readonly RealmMongoDatabaseProvider? databases;

    public MongoCharacterQuestRepository(IMongoClient client, IOptions<MongoOptions> options) =>
        legacyDatabase = client.GetDatabase(options.Value.DatabaseName);

    [ActivatorUtilitiesConstructor]
    public MongoCharacterQuestRepository(RealmMongoDatabaseProvider databases)
    {
        this.databases = databases;
        legacyDatabase = databases.LegacyDatabase;
    }

    private static IMongoCollection<CharacterQuestDocument> Quests(IMongoDatabase database) =>
        database.GetCollection<CharacterQuestDocument>("character_quests");

    public async Task EnsureIndexesAsync(CancellationToken cancellationToken)
    {
        foreach (var database in databases?.GameplayDatabases ?? [legacyDatabase])
            await Quests(database).Indexes.CreateOneAsync(
                new CreateIndexModel<CharacterQuestDocument>(Builders<CharacterQuestDocument>.IndexKeys
                    .Ascending(quest => quest.CharacterId).Ascending(quest => quest.QuestId),
                    new CreateIndexOptions { Name = "ux_character_quest", Unique = true }), cancellationToken: cancellationToken);
    }

    private Task<IMongoDatabase?> ResolveDatabaseAsync(Guid characterId, CancellationToken cancellationToken) =>
        databases is null ? Task.FromResult<IMongoDatabase?>(legacyDatabase) : databases.ResolveCharacterDatabaseAsync(characterId, cancellationToken);

    public async Task<IReadOnlyList<CharacterQuest>> ListAsync(Guid characterId, CancellationToken cancellationToken)
    {
        var database = await ResolveDatabaseAsync(characterId, cancellationToken);
        if (database is null) return [];
        return (await Quests(database).Find(quest => quest.CharacterId == characterId.ToString("D"))
            .SortBy(quest => quest.QuestId).ToListAsync(cancellationToken)).Select(quest => quest.ToDomain()).ToArray();
    }

    public async Task<CharacterQuest?> FindAsync(Guid characterId, string questId, CancellationToken cancellationToken)
    {
        var database = await ResolveDatabaseAsync(characterId, cancellationToken);
        return database is null ? null : (await Quests(database).Find(quest => quest.CharacterId == characterId.ToString("D") && quest.QuestId == questId)
            .FirstOrDefaultAsync(cancellationToken))?.ToDomain();
    }

    public async Task<bool> TryWriteAsync(CharacterQuest quest, long expectedRevision, CancellationToken cancellationToken)
    {
        var database = await ResolveDatabaseAsync(quest.CharacterId, cancellationToken);
        if (database is null) return false;
        var quests = Quests(database);
        var document = CharacterQuestDocument.From(quest);
        if (expectedRevision == 0)
        {
            try
            {
                await quests.InsertOneAsync(document, cancellationToken: cancellationToken);
                return true;
            }
            catch (MongoWriteException exception) when (exception.WriteError.Category == ServerErrorCategory.DuplicateKey)
            {
                return false;
            }
        }

        var result = await quests.ReplaceOneAsync(existing => existing.Id == document.Id && existing.Revision == expectedRevision,
            document, cancellationToken: cancellationToken);
        return result.ModifiedCount == 1;
    }
}
