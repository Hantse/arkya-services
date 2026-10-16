using Arkya.Characters.Api.Application;
using Arkya.Characters.Api.Domain;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Arkya.Characters.Api.Infrastructure.Persistence;

public sealed class MongoCharacterQuestRepository(IMongoClient client, IOptions<MongoOptions> options) : ICharacterQuestRepository
{
    private readonly IMongoCollection<CharacterQuestDocument> quests = client
        .GetDatabase(options.Value.DatabaseName).GetCollection<CharacterQuestDocument>("character_quests");

    public Task EnsureIndexesAsync(CancellationToken cancellationToken) => quests.Indexes.CreateOneAsync(
        new CreateIndexModel<CharacterQuestDocument>(Builders<CharacterQuestDocument>.IndexKeys
            .Ascending(quest => quest.CharacterId).Ascending(quest => quest.QuestId),
            new CreateIndexOptions { Name = "ux_character_quest", Unique = true }), cancellationToken: cancellationToken);

    public async Task<IReadOnlyList<CharacterQuest>> ListAsync(Guid characterId, CancellationToken cancellationToken) =>
        (await quests.Find(quest => quest.CharacterId == characterId.ToString("D"))
            .SortBy(quest => quest.QuestId).ToListAsync(cancellationToken)).Select(quest => quest.ToDomain()).ToArray();

    public async Task<CharacterQuest?> FindAsync(Guid characterId, string questId, CancellationToken cancellationToken) =>
        (await quests.Find(quest => quest.CharacterId == characterId.ToString("D") && quest.QuestId == questId)
            .FirstOrDefaultAsync(cancellationToken))?.ToDomain();

    public async Task<bool> TryWriteAsync(CharacterQuest quest, long expectedRevision, CancellationToken cancellationToken)
    {
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
