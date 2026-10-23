using Arkya.Realms.Api.Application;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Options;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace Arkya.Realms.Api.Infrastructure;

public sealed class MongoRealmCommandRepository(IMongoClient client, IOptions<MongoOptions> options) : IRealmCommandRepository
{
    private readonly IMongoCollection<RealmCommandDocument> collection = client.GetDatabase(options.Value.DatabaseName)
        .GetCollection<RealmCommandDocument>("realm_commands").WithWriteConcern(WriteConcern.WMajority.With(journal: true));
    // The compound identity is the unique Mongo _id; each server keeps at most 100 audit entries.
    public Task InitializeAsync(CancellationToken ct) => Task.CompletedTask;

    public async Task<RealmCommandServer?> FindAsync(string realmId, string serverId, CancellationToken ct)
    {
        var document = await collection.Find(item => item.Id == realmId + "/" + serverId).FirstOrDefaultAsync(ct);
        if (document is null) return null;
        if (document.SchemaVersion != 1) throw new InvalidOperationException("Unsupported realm command schema.");
        return document.State;
    }

    public async Task<bool> TryWriteAsync(RealmCommandServer server, long? expectedVersion, CancellationToken ct)
    {
        var document = new RealmCommandDocument { Id = server.RealmId + "/" + server.ServerId, State = server };
        if (expectedVersion is not null)
        {
            var result = await collection.ReplaceOneAsync(item => item.Id == document.Id && item.State.Version == expectedVersion,
                document, cancellationToken: ct);
            return result.ModifiedCount == 1;
        }
        try { await collection.InsertOneAsync(document, cancellationToken: ct); return true; }
        catch (MongoWriteException exception) when (exception.WriteError.Category == ServerErrorCategory.DuplicateKey) { return false; }
    }
}

internal sealed class RealmCommandDocument
{
    [BsonId] public string Id { get; set; } = "";
    public int SchemaVersion { get; set; } = 1;
    public RealmCommandServer State { get; set; } = null!;
}
