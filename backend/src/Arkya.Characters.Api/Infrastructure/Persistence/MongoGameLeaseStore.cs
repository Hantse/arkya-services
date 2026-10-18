using Arkya.Characters.Api.Application;
using Arkya.Characters.Api.Contracts;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace Arkya.Characters.Api.Infrastructure.Persistence;

public sealed class MongoGameLeaseStore(IMongoClient client, IOptions<MongoOptions> options) : IGameLeaseStore
{
    private readonly IMongoCollection<GameLeaseDocument> leases = client.GetDatabase(options.Value.DatabaseName).GetCollection<GameLeaseDocument>("game_leases");
    public async Task EnsureIndexesAsync(CancellationToken cancellationToken) => await leases.Indexes.CreateOneAsync(
        new CreateIndexModel<GameLeaseDocument>(Builders<GameLeaseDocument>.IndexKeys.Ascending(value => value.TicketHash),
            new CreateIndexOptions<GameLeaseDocument> { Name = "ux_admission_ticket", Unique = true,
                PartialFilterExpression = Builders<GameLeaseDocument>.Filter.Type(value => value.TicketHash, MongoDB.Bson.BsonType.String) }), cancellationToken: cancellationToken);
    public async Task<GameLeaseRecord?> FindCharacterAsync(Guid characterId, CancellationToken cancellationToken) =>
        (await leases.Find(value => value.CharacterId == characterId.ToString("D")).FirstOrDefaultAsync(cancellationToken))?.ToRecord();
    public async Task<GameLeaseRecord?> FindTicketAsync(string ticketHash, CancellationToken cancellationToken) =>
        (await leases.Find(value => value.TicketHash == ticketHash).FirstOrDefaultAsync(cancellationToken))?.ToRecord();
    public async Task<bool> TryWriteAsync(GameLeaseRecord record, long? expectedVersion, CancellationToken cancellationToken, GameWriteCondition condition = GameWriteCondition.None)
    {
        var document = GameLeaseDocument.From(record);
        if (expectedVersion is null)
        {
            try { await leases.InsertOneAsync(document, cancellationToken: cancellationToken); return true; }
            catch (MongoWriteException error) when (error.WriteError.Category == ServerErrorCategory.DuplicateKey) { return false; }
        }
        var filter = Builders<GameLeaseDocument>.Filter.Where(value => value.CharacterId == document.CharacterId && value.Version == expectedVersion.Value);
        if (condition != GameWriteCondition.None)
        {
            // Mongo checks its clock at the atomic write, including time spent waiting for IO.
            var expiryField = condition == GameWriteCondition.PendingTicket ? "$TicketExpiresAt" : "$LeaseExpiresAt";
            filter &= new BsonDocument("$expr", new BsonDocument("$and", new BsonArray
            {
                new BsonDocument("$gt", new BsonArray { expiryField, "$$NOW" }),
                new BsonDocument("$gt", new BsonArray { "$SessionExpiresAt", "$$NOW" })
            }));
        }
        var result = await leases.ReplaceOneAsync(filter, document, cancellationToken: cancellationToken);
        return result.MatchedCount == 1;
    }

    private sealed class GameLeaseDocument
    {
        [BsonId] public string CharacterId { get; set; } = "";
        public int SchemaVersion { get; set; } = 2;
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
        public SavedWorldState? State { get; set; }
        public GameLeaseRecord ToRecord()
        {
            if (SchemaVersion is not (1 or 2)) throw new InvalidDataException("Unsupported game lease schema.");
            return new(Guid.Parse(CharacterId), Guid.Parse(AccountId), RealmId, ServerId, Version, TicketHash,
                Utc(TicketExpiresAt), Guid.Parse(LeaseId), AuthorityEpoch, Utc(LeaseExpiresAt), Utc(SessionExpiresAt), State, SessionReference);
        }
        private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
        public static GameLeaseDocument From(GameLeaseRecord value) => new()
        {
            CharacterId = value.CharacterId.ToString("D"), AccountId = value.AccountId.ToString("D"), RealmId = value.RealmId,
            ServerId = value.ServerId, Version = value.Version, TicketHash = value.TicketHash, TicketExpiresAt = value.TicketExpiresAt.UtcDateTime,
            LeaseId = value.LeaseId.ToString("D"), AuthorityEpoch = value.AuthorityEpoch, LeaseExpiresAt = value.LeaseExpiresAt.UtcDateTime,
            SessionExpiresAt = value.SessionExpiresAt.UtcDateTime, State = value.State, SessionReference = value.SessionReference
        };
    }
}
