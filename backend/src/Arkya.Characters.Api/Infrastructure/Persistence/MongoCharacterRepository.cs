using Arkya.Characters.Api.Application;
using Arkya.Characters.Api.Domain;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Arkya.Characters.Api.Infrastructure.Persistence;

public sealed class MongoCharacterRepository(IMongoClient client, IOptions<MongoOptions> options) : ICharacterRepository
{
    private readonly IMongoCollection<CharacterDocument> characters = client
        .GetDatabase(options.Value.DatabaseName).GetCollection<CharacterDocument>("characters");

    public async Task EnsureIndexesAsync(CancellationToken cancellationToken)
    {
        await characters.Indexes.CreateManyAsync([
            new CreateIndexModel<CharacterDocument>(
                Builders<CharacterDocument>.IndexKeys.Ascending(character => character.NormalizedName),
                new CreateIndexOptions { Name = "ux_character_normalized_name", Unique = true }),
            new CreateIndexModel<CharacterDocument>(Builders<CharacterDocument>.IndexKeys
                .Ascending(character => character.AccountId)
                .Ascending(character => character.CreatedAt)
                .Ascending(character => character.Id), new CreateIndexOptions { Name = "ix_character_owner_created" })
        ], cancellationToken);
    }

    public async Task<IReadOnlyList<Character>> ListOwnedAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var documents = await characters.Find(character => character.AccountId == accountId.ToString("D"))
            .SortBy(character => character.CreatedAt).ThenBy(character => character.Id)
            .ToListAsync(cancellationToken);
        return documents.Select(character => character.ToDomain()).ToArray();
    }

    public Task<bool> ExistsAsync(Guid characterId, CancellationToken cancellationToken) =>
        characters.Find(character => character.Id == characterId.ToString("D")).AnyAsync(cancellationToken);

    public async Task<Character?> FindOwnedAsync(Guid characterId, Guid accountId, CancellationToken cancellationToken)
    {
        var document = await characters.Find(character => character.Id == characterId.ToString("D") &&
            character.AccountId == accountId.ToString("D")).FirstOrDefaultAsync(cancellationToken);
        return document?.ToDomain();
    }

    public async Task<bool> TryAddAsync(Character character, CancellationToken cancellationToken)
    {
        try
        {
            await characters.InsertOneAsync(CharacterDocument.From(character), cancellationToken: cancellationToken);
            return true;
        }
        catch (MongoWriteException exception) when (exception.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }
}
