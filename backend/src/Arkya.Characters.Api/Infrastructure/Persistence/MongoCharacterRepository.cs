using Arkya.Characters.Api.Application;
using Arkya.Characters.Api.Domain;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Arkya.Characters.Api.Infrastructure.Persistence;

public sealed class MongoCharacterRepository : ICharacterRepository
{
    private readonly RealmMongoDatabaseProvider storage;
    private IMongoCollection<CharacterDocument> characters => storage.LegacyDatabase.GetCollection<CharacterDocument>("characters");
    private IMongoCollection<BsonDocument> directory => storage.DirectoryDatabase
        .GetCollection<BsonDocument>("character_directory").WithWriteConcern(WriteConcern.WMajority.With(journal: true));

    public MongoCharacterRepository(IMongoClient client, IOptions<MongoOptions> options)
        : this(new RealmMongoDatabaseProvider(client, options, Options.Create(new RealmStorageOptions()))) { }

    public MongoCharacterRepository(RealmMongoDatabaseProvider storage) => this.storage = storage;

    public async Task EnsureIndexesAsync(CancellationToken cancellationToken)
    {
        if (storage.Enabled)
        {
            await directory.Indexes.CreateManyAsync([
                new CreateIndexModel<BsonDocument>(new BsonDocument("NormalizedName", 1),
                    new CreateIndexOptions { Name = "ux_directory_normalized_name", Unique = true }),
                new CreateIndexModel<BsonDocument>(new BsonDocument("AccountId", 1),
                    new CreateIndexOptions { Name = "ix_directory_owner" })
            ], cancellationToken);
        }
        foreach (var database in storage.GameplayDatabases)
        await database.GetCollection<CharacterDocument>("characters").Indexes.CreateManyAsync([
            new CreateIndexModel<CharacterDocument>(
                Builders<CharacterDocument>.IndexKeys.Ascending(character => character.NormalizedName),
                new CreateIndexOptions { Name = "ux_character_normalized_name", Unique = true }),
            new CreateIndexModel<CharacterDocument>(Builders<CharacterDocument>.IndexKeys
                .Ascending(character => character.AccountId)
                .Ascending(character => character.CreatedAt)
                .Ascending(character => character.Id), new CreateIndexOptions { Name = "ix_character_owner_created" }),
            new CreateIndexModel<CharacterDocument>(Builders<CharacterDocument>.IndexKeys
                .Ascending(character => character.RealmId)
                .Descending(character => character.CreatedAt)
                .Ascending(character => character.Id), new CreateIndexOptions { Name = "ix_character_realm_created" })
        ], cancellationToken);
    }

    public async Task<IReadOnlyList<Character>> ListOwnedAsync(Guid accountId, CancellationToken cancellationToken)
    {
        if (storage.Enabled)
        {
            var entries = await directory.Find(new BsonDocument("AccountId", accountId.ToString("D")))
                .ToListAsync(cancellationToken);
            var result = new List<Character>();
            foreach (var entry in entries) result.Add(await ReadRoutedAsync(entry, cancellationToken));
            return result.OrderBy(value => value.CreatedAt).ThenBy(value => value.Id).ToArray();
        }
        var documents = await characters.Find(character => character.AccountId == accountId.ToString("D"))
            .SortBy(character => character.CreatedAt).ThenBy(character => character.Id)
            .ToListAsync(cancellationToken);
        return documents.Select(character => character.ToDomain()).ToArray();
    }

    public async Task<bool> ExistsAsync(Guid characterId, CancellationToken cancellationToken)
    {
        if (!storage.Enabled) return await characters.Find(character => character.Id == characterId.ToString("D")).AnyAsync(cancellationToken);
        var entry = await storage.FindDirectoryEntryAsync(characterId, cancellationToken);
        if (entry is null) return false;
        _ = await ReadRoutedAsync(entry, cancellationToken);
        return true;
    }

    public async Task<bool> TryBindRealmAsync(Guid characterId, Guid accountId, string realmId, CancellationToken cancellationToken)
    {
        if (storage.Enabled)
        {
            var character = await FindOwnedAsync(characterId, accountId, cancellationToken);
            return character is not null && character.RealmId == realmId;
        }
        var result = await characters.UpdateOneAsync(value => value.Id == characterId.ToString("D") &&
            value.AccountId == accountId.ToString("D") && (value.RealmId == null || value.RealmId == realmId),
            Builders<CharacterDocument>.Update.Set(value => value.RealmId, realmId), cancellationToken: cancellationToken);
        return result.MatchedCount == 1;
    }

    public async Task<Character?> FindOwnedAsync(Guid characterId, Guid accountId, CancellationToken cancellationToken)
    {
        if (storage.Enabled)
        {
            var entry = await directory.Find(new BsonDocument
            {
                ["_id"] = characterId.ToString("D"), ["AccountId"] = accountId.ToString("D")
            }).FirstOrDefaultAsync(cancellationToken);
            return entry is null ? null : await ReadRoutedAsync(entry, cancellationToken);
        }
        var document = await characters.Find(character => character.Id == characterId.ToString("D") &&
            character.AccountId == accountId.ToString("D")).FirstOrDefaultAsync(cancellationToken);
        return document?.ToDomain();
    }

    public async Task<bool> TryAddAsync(Character character, CancellationToken cancellationToken)
    {
        if (storage.Enabled)
        {
            if (character.RealmId is null) throw new RealmStorageUnavailableException();
            _ = storage.GetRealmDatabase(character.RealmId);
            var entry = new BsonDocument
            {
                ["_id"] = character.Id.ToString("D"), ["AccountId"] = character.AccountId.ToString("D"),
                ["RealmId"] = character.RealmId, ["NormalizedName"] = character.NormalizedName,
                ["Status"] = "Pending", ["CreationIntent"] = CharacterDocument.From(character).ToBsonDocument()
            };
            try { await directory.InsertOneAsync(entry, cancellationToken: cancellationToken); }
            catch (MongoWriteException exception) when (exception.WriteError.Category == ServerErrorCategory.DuplicateKey)
            {
                var previous = await storage.FindDirectoryEntryAsync(character.Id, cancellationToken);
                if (previous is null || previous.GetValue("AccountId", BsonNull.Value) != entry["AccountId"] ||
                    previous.GetValue("RealmId", BsonNull.Value) != entry["RealmId"] ||
                    previous.GetValue("NormalizedName", BsonNull.Value) != entry["NormalizedName"]) return false;
                if (previous.GetValue("Status", "") == "Pending" &&
                    !previous.GetValue("CreationIntent", BsonNull.Value).Equals(entry["CreationIntent"])) return false;
                var existing = await ReadRoutedAsync(previous, cancellationToken);
                return CharacterDocument.From(existing).ToBsonDocument().Equals(entry["CreationIntent"]);
            }
            await ReadRoutedAsync(entry, cancellationToken);
            return true;
        }
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

    private async Task<Character> ReadRoutedAsync(BsonDocument entry, CancellationToken ct)
    {
        var realm = entry.GetValue("RealmId", BsonNull.Value);
        if (!realm.IsString) throw new RealmStorageUnavailableException();
        var collection = storage.GetRealmDatabase(realm.AsString).GetCollection<CharacterDocument>("characters")
            .WithWriteConcern(WriteConcern.WMajority.With(journal: true));
        var id = entry["_id"].AsString;
        if (entry["Status"] == "Pending")
        {
            if (!entry.TryGetValue("CreationIntent", out var intent) || !intent.IsBsonDocument)
                throw new RealmStorageUnavailableException();
            var initial = MongoDB.Bson.Serialization.BsonSerializer.Deserialize<CharacterDocument>(intent.AsBsonDocument);
            if (initial.Id != id || initial.RealmId != realm.AsString || initial.AccountId != entry["AccountId"].AsString ||
                initial.NormalizedName != entry["NormalizedName"].AsString) throw new RealmStorageUnavailableException();
            // The durable intent survives a crash between name reservation and realm creation.
            await collection.UpdateOneAsync(value => value.Id == id,
                new BsonDocument("$setOnInsert", intent.AsBsonDocument),
                new UpdateOptions { IsUpsert = true }, ct);
        }
        var document = await collection.Find(value => value.Id == id).FirstOrDefaultAsync(ct);
        if (document is null || document.RealmId != realm.AsString || document.AccountId != entry["AccountId"].AsString ||
            document.NormalizedName != entry["NormalizedName"].AsString) throw new RealmStorageUnavailableException();
        if (entry["Status"] == "Pending")
            await directory.UpdateOneAsync(new BsonDocument { ["_id"] = id, ["Status"] = "Pending" },
                new BsonDocument
                {
                    ["$set"] = new BsonDocument("Status", "Ready"),
                    ["$unset"] = new BsonDocument("CreationIntent", "")
                }, cancellationToken: ct);
        else if (entry["Status"] != "Ready") throw new RealmStorageUnavailableException();
        return document.ToDomain();
    }
}
