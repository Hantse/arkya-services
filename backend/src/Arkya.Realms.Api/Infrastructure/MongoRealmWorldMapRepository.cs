using Arkya.Realms.Api.Application;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Options;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace Arkya.Realms.Api.Infrastructure;

public sealed class MongoRealmWorldMapRepository(IMongoClient client, IOptions<MongoOptions> options) : IRealmWorldMapRepository
{
    private readonly IMongoCollection<RealmWorldMapDocument> collection = client.GetDatabase(options.Value.DatabaseName)
        .GetCollection<RealmWorldMapDocument>("realm_world_maps").WithWriteConcern(WriteConcern.WMajority.With(journal: true));

    public async Task<RealmWorldMapSnapshot[]> FindAsync(string realmId, string[] serverIds, CancellationToken ct)
    {
        var ids = serverIds.Select(serverId => realmId + "/" + serverId).ToArray();
        var documents = await collection.Find(Builders<RealmWorldMapDocument>.Filter.In(item => item.Id, ids)).ToListAsync(ct);
        if (documents.Any(document => document.SchemaVersion != 1)) throw new InvalidOperationException("Unsupported realm world map schema.");
        return documents.Select(document => document.State).ToArray();
    }

    public async Task ReplaceAsync(RealmWorldMapSnapshot snapshot, CancellationToken ct)
    {
        // A compound _id keeps one bounded snapshot per server, without a location history.
        var document = new RealmWorldMapDocument
        {
            Id = snapshot.RealmId + "/" + snapshot.ServerId, ObservedAtUtc = snapshot.UpdatedAt.UtcDateTime, State = snapshot
        };
        try
        {
            await collection.ReplaceOneAsync(item => item.Id == document.Id && item.ObservedAtUtc <= document.ObservedAtUtc,
                document, new ReplaceOptions { IsUpsert = true }, ct);
        }
        catch (MongoWriteException exception) when (exception.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            // A newer observation already won a concurrent request.
        }
    }
}

internal sealed class RealmWorldMapDocument
{
    [BsonId] public string Id { get; set; } = "";
    public int SchemaVersion { get; set; } = 1;
    public DateTime ObservedAtUtc { get; set; }
    public RealmWorldMapSnapshot State { get; set; } = null!;
}
