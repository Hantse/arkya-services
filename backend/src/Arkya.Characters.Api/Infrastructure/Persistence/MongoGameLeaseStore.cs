using Arkya.Characters.Api.Application;
using Arkya.Characters.Api.Contracts;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace Arkya.Characters.Api.Infrastructure.Persistence;

public sealed class MongoGameLeaseStore : IGameLeaseStore
{
    private readonly IMongoDatabase legacyDatabase;
    private readonly RealmMongoDatabaseProvider? databases;

    public MongoGameLeaseStore(IMongoClient client, IOptions<MongoOptions> options) =>
        legacyDatabase = client.GetDatabase(options.Value.DatabaseName);

    [ActivatorUtilitiesConstructor]
    public MongoGameLeaseStore(RealmMongoDatabaseProvider databases)
    {
        this.databases = databases;
        legacyDatabase = databases.LegacyDatabase;
    }

    private static IMongoCollection<GameLeaseDocument> Leases(IMongoDatabase database) =>
        database.GetCollection<GameLeaseDocument>("game_leases").WithWriteConcern(WriteConcern.WMajority.With(journal: true));

    public async Task EnsureIndexesAsync(CancellationToken cancellationToken)
    {
        foreach (var database in databases?.GameplayDatabases ?? [legacyDatabase])
            await Leases(database).Indexes.CreateManyAsync([
        new CreateIndexModel<GameLeaseDocument>(Builders<GameLeaseDocument>.IndexKeys.Ascending(value => value.TicketHash),
            new CreateIndexOptions<GameLeaseDocument> { Name = "ux_admission_ticket", Unique = true,
                PartialFilterExpression = Builders<GameLeaseDocument>.Filter.Type(value => value.TicketHash, MongoDB.Bson.BsonType.String) }),
        new CreateIndexModel<GameLeaseDocument>(Builders<GameLeaseDocument>.IndexKeys.Ascending(value => value.AccountId)
            .Descending(value => value.LastConnectedAt).Ascending(value => value.CharacterId),
            new CreateIndexOptions { Name = "ix_account_last_connection" })], cancellationToken: cancellationToken);
    }

    public async Task<GameLeaseRecord?> FindCharacterAsync(Guid characterId, CancellationToken cancellationToken)
    {
        var database = databases is null ? legacyDatabase : await databases.ResolveCharacterDatabaseAsync(characterId, cancellationToken);
        if (database is null) return null;
        var document = await Leases(database).Find(value => value.CharacterId == characterId.ToString("D"))
            .FirstOrDefaultAsync(cancellationToken);
        return document is not null && await DirectoryOwnsAsync(document, cancellationToken) ? document.ToRecord() : null;
    }

    public async Task<GameLeaseRecord?> FindTicketAsync(string ticketHash, CancellationToken cancellationToken)
    {
        if (databases?.Enabled == true)
            throw new InvalidOperationException("Realm-scoped ticket lookup is required when realm storage is enabled.");
        return (await Leases(legacyDatabase).Find(value => value.TicketHash == ticketHash)
            .FirstOrDefaultAsync(cancellationToken))?.ToRecord();
    }

    public async Task<GameLeaseRecord?> FindTicketAsync(string ticketHash, string realmId, CancellationToken cancellationToken)
    {
        var database = databases?.Enabled == true ? databases.GetRealmDatabase(realmId) : legacyDatabase;
        var document = await Leases(database).Find(value => value.TicketHash == ticketHash && value.RealmId == realmId)
            .FirstOrDefaultAsync(cancellationToken);
        return document is not null && await DirectoryOwnsAsync(document, cancellationToken) ? document.ToRecord() : null;
    }
    public async Task<GameLeaseRecord?> FindLastConnectedAsync(Guid accountId, IReadOnlyCollection<Guid> characterIds, CancellationToken cancellationToken)
    {
        GameLeaseRecord? latest = null;
        foreach (var batch in characterIds.Distinct().Chunk(256))
        {
            var candidate = await FindLastConnectedBatchAsync(accountId, batch, cancellationToken);
            if (IsLater(candidate, latest)) latest = candidate;
        }
        return latest;
    }

    private async Task<GameLeaseRecord?> FindLastConnectedBatchAsync(Guid accountId, Guid[] characterIds, CancellationToken cancellationToken)
    {
        if (databases?.Enabled != true)
            return await FindLastConnectedAsync(legacyDatabase, accountId, characterIds.Select(value => value.ToString("D")), null, cancellationToken);

        var directory = databases.DirectoryDatabase.GetCollection<BsonDocument>("character_directory");
        var entries = await directory.Find(Builders<BsonDocument>.Filter.Eq("AccountId", accountId.ToString("D")) &
            Builders<BsonDocument>.Filter.In("_id", characterIds.Select(value => value.ToString("D"))))
            .Project(new BsonDocument { ["_id"] = 1, ["RealmId"] = 1 }).ToListAsync(cancellationToken);
        if (entries.Any(value => !value.GetValue("RealmId", BsonNull.Value).IsString))
            throw new RealmStorageUnavailableException();
        GameLeaseRecord? latest = null;
        foreach (var group in entries.GroupBy(value => value["RealmId"].AsString, StringComparer.Ordinal))
        {
            var candidate = await FindLastConnectedAsync(databases.GetRealmDatabase(group.Key), accountId,
                group.Select(value => value["_id"].AsString), group.Key, cancellationToken);
            if (IsLater(candidate, latest)) latest = candidate;
        }
        return latest;
    }

    private static bool IsLater(GameLeaseRecord? candidate, GameLeaseRecord? current) => candidate is not null &&
        (current is null || candidate.LastConnectedAt > current.LastConnectedAt || candidate.LastConnectedAt == current.LastConnectedAt &&
            string.CompareOrdinal(candidate.CharacterId.ToString("D"), current.CharacterId.ToString("D")) < 0);

    private static async Task<GameLeaseRecord?> FindLastConnectedAsync(IMongoDatabase database, Guid accountId,
        IEnumerable<string> characterIds, string? realmId, CancellationToken cancellationToken)
    {
        var filter = Builders<GameLeaseDocument>.Filter.Eq(value => value.AccountId, accountId.ToString("D")) &
            Builders<GameLeaseDocument>.Filter.In(value => value.CharacterId, characterIds) &
            Builders<GameLeaseDocument>.Filter.Ne(value => value.LastConnectedAt, null);
        if (realmId is not null) filter &= Builders<GameLeaseDocument>.Filter.Eq(value => value.RealmId, realmId);
        return (await Leases(database).Find(filter).SortByDescending(value => value.LastConnectedAt).ThenBy(value => value.CharacterId)
            .Limit(1).FirstOrDefaultAsync(cancellationToken))?.ToRecord();
    }

    private async Task<bool> DirectoryOwnsAsync(GameLeaseDocument document, CancellationToken cancellationToken)
    {
        if (databases?.Enabled != true) return true;
        var entry = await databases.FindDirectoryEntryAsync(Guid.Parse(document.CharacterId), cancellationToken);
        return entry is not null && entry.GetValue("RealmId", BsonNull.Value) == new BsonString(document.RealmId) &&
            entry.GetValue("AccountId", BsonNull.Value) == new BsonString(document.AccountId);
    }
    public async Task<bool> TryWriteAsync(GameLeaseRecord record, long? expectedVersion, CancellationToken cancellationToken, GameWriteCondition condition = GameWriteCondition.None)
    {
        var document = GameLeaseDocument.From(record);
        if (!await DirectoryOwnsAsync(document, cancellationToken)) return false;
        var leases = Leases(databases?.Enabled == true ? databases.GetRealmDatabase(record.RealmId) : legacyDatabase);
        if (expectedVersion is null)
        {
            try { await leases.InsertOneAsync(document, cancellationToken: cancellationToken); return true; }
            catch (MongoWriteException error) when (error.WriteError.Category == ServerErrorCategory.DuplicateKey) { return false; }
        }
        var filter = Builders<GameLeaseDocument>.Filter.Where(value => value.CharacterId == document.CharacterId &&
            value.AccountId == document.AccountId && value.RealmId == document.RealmId && value.Version == expectedVersion.Value);
        if (condition != GameWriteCondition.None)
        {
            // Mongo checks its clock at the atomic write, including time spent waiting for IO.
            var expiryField = condition == GameWriteCondition.PendingTicket ? "$TicketExpiresAt" : "$LeaseExpiresAt";
            filter &= new BsonDocument("$expr", new BsonDocument("$and", new BsonArray
            {
                new BsonDocument("$gt", new BsonArray { expiryField, "$$NOW" }),
                new BsonDocument("$gt", new BsonArray { "$SessionExpiresAt", "$$NOW" })
            }));
            if (condition == GameWriteCondition.TransferPending)
                filter &= new BsonDocument("$expr", new BsonDocument("$gt", new BsonArray { "$TransferExpiresAt", "$$NOW" }));
        }
        var result = await leases.ReplaceOneAsync(filter, document, cancellationToken: cancellationToken);
        return result.MatchedCount == 1;
    }

    private sealed class GameLeaseDocument
    {
        [BsonId] public string CharacterId { get; set; } = "";
        public int SchemaVersion { get; set; } = 4;
        public string AccountId { get; set; } = "";
        public string RealmId { get; set; } = "";
        public string ServerId { get; set; } = "";
        public long Version { get; set; }
        public string? TicketHash { get; set; }
        public DateTime TicketExpiresAt { get; set; }
        public string LeaseId { get; set; } = "";
        public long AuthorityEpoch { get; set; }
        public DateTime LeaseExpiresAt { get; set; }
        public DateTime SessionExpiresAt { get; set; }
        public string? SessionReference { get; set; }
        public DateTime? LastConnectedAt { get; set; }
        public SavedWorldState? State { get; set; }
        [BsonIgnoreIfNull] public GameRealmArea? Area { get; set; }
        public GameTransferRecord? Transfer { get; set; }
        public DateTime? TransferExpiresAt { get; set; }
        public GameLeaseRecord ToRecord()
        {
            if (SchemaVersion is not (1 or 2 or 3 or 4 or 5 or 6)) throw new InvalidDataException("Unsupported game lease schema.");
            return new(Guid.Parse(CharacterId), Guid.Parse(AccountId), RealmId, ServerId, Version, TicketHash,
                Utc(TicketExpiresAt), Guid.Parse(LeaseId), AuthorityEpoch, Utc(LeaseExpiresAt), Utc(SessionExpiresAt), State, SessionReference,
                LastConnectedAt is { } lastConnectedAt ? Utc(lastConnectedAt) : null) { Transfer = Transfer, Area = Area };
        }
        private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
        public static GameLeaseDocument From(GameLeaseRecord value) => new()
        {
            SchemaVersion = value.State?.Possessions is not null || value.Transfer?.State.Possessions is not null ? 6 :
                value.Area is not null || value.Transfer?.Transition is not null ? 5 : value.Transfer is null && value.State?.Simulation is null ? 3 : 4,
            CharacterId = value.CharacterId.ToString("D"), AccountId = value.AccountId.ToString("D"), RealmId = value.RealmId,
            ServerId = value.ServerId, Version = value.Version, TicketHash = value.TicketHash, TicketExpiresAt = value.TicketExpiresAt.UtcDateTime,
            LeaseId = value.LeaseId.ToString("D"), AuthorityEpoch = value.AuthorityEpoch, LeaseExpiresAt = value.LeaseExpiresAt.UtcDateTime,
            SessionExpiresAt = value.SessionExpiresAt.UtcDateTime, State = value.State, Area = value.Area, SessionReference = value.SessionReference,
            LastConnectedAt = value.LastConnectedAt?.UtcDateTime, Transfer = value.Transfer, TransferExpiresAt = value.Transfer?.ExpiresAt.UtcDateTime
        };
    }
}
