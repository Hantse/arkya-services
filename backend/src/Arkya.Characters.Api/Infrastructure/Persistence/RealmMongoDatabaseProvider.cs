using Arkya.Characters.Api.Application;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Security.Cryptography;
using System.Text;

namespace Arkya.Characters.Api.Infrastructure.Persistence;

public sealed class RealmMongoDatabaseProvider : IRealmStorageRouting
{
    private readonly IReadOnlyDictionary<string, IMongoDatabase> databases;
    public bool Enabled { get; }
    public IMongoDatabase LegacyDatabase { get; }
    public IMongoDatabase DirectoryDatabase => LegacyDatabase;
    public IReadOnlyList<RealmStorageRoute> Routes { get; }
    public IEnumerable<IMongoDatabase> GameplayDatabases => Enabled ? databases.Values : [LegacyDatabase];

    public RealmMongoDatabaseProvider(IMongoClient client, IOptions<MongoOptions> mongo, IOptions<RealmStorageOptions> storage)
    {
        if (!storage.Value.IsValid(mongo.Value.DatabaseName)) throw new RealmStorageUnavailableException();
        Enabled = storage.Value.Enabled;
        LegacyDatabase = client.GetDatabase(mongo.Value.DatabaseName);
        Routes = storage.Value.Realms.Select(route => new RealmStorageRoute
        {
            RealmId = route.RealmId, DatabaseName = route.DatabaseName, ConnectionString = route.ConnectionString
        }).ToArray();
        var clients = new Dictionary<string, IMongoClient>(StringComparer.Ordinal);
        databases = Routes.ToDictionary(route => route.RealmId, route =>
        {
            if (!clients.TryGetValue(route.ConnectionString, out var target))
            {
                var settings = MongoClientSettings.FromConnectionString(route.ConnectionString);
                settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
                settings.ConnectTimeout = TimeSpan.FromSeconds(5);
                target = new MongoClient(settings);
                clients.Add(route.ConnectionString, target);
            }
            return target.GetDatabase(route.DatabaseName);
        }, StringComparer.Ordinal);
    }

    public IMongoDatabase GetRealmDatabase(string realmId) =>
        databases.TryGetValue(realmId, out var database) ? database : throw new RealmStorageUnavailableException();

    public bool IsConfigured(string realmId) => !Enabled || databases.ContainsKey(realmId);

    public static string RouteFingerprint(RealmStorageRoute route)
    {
        var settings = MongoClientSettings.FromConnectionString(route.ConnectionString);
        var identity = string.Join(",", settings.Servers.Select(server => server.ToString()).Order(StringComparer.Ordinal)) +
            "|" + settings.ReplicaSetName + "|" + route.DatabaseName;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }

    public async Task<BsonDocument?> FindDirectoryEntryAsync(Guid characterId, CancellationToken ct) =>
        await DirectoryDatabase.GetCollection<BsonDocument>("character_directory")
            .Find(new BsonDocument("_id", characterId.ToString("D"))).FirstOrDefaultAsync(ct);

    public async Task<string?> FindCharacterRealmAsync(Guid characterId, CancellationToken ct)
    {
        var entry = await FindDirectoryEntryAsync(characterId, ct);
        if (entry is null) return null;
        if (!entry.TryGetValue("RealmId", out var realm) || !realm.IsString || !databases.ContainsKey(realm.AsString))
            throw new RealmStorageUnavailableException();
        return realm.AsString;
    }

    public async Task<IMongoDatabase?> ResolveCharacterDatabaseAsync(Guid characterId, CancellationToken ct)
    {
        if (!Enabled) return LegacyDatabase;
        var realmId = await FindCharacterRealmAsync(characterId, ct);
        return realmId is null ? null : GetRealmDatabase(realmId);
    }

    public async Task EnsureActivatedAsync(CancellationToken ct)
    {
        if (!Enabled) return;
        var markers = DirectoryDatabase.GetCollection<BsonDocument>("realm_storage_migrations");
        foreach (var route in Routes)
        {
            var marker = await markers.Find(new BsonDocument
            {
                ["_id"] = route.RealmId, ["DatabaseName"] = route.DatabaseName, ["Status"] = "Complete",
                ["RouteFingerprint"] = RouteFingerprint(route)
            }).AnyAsync(ct);
            if (!marker) throw new InvalidOperationException("Complete the offline realm storage migration before enabling isolated storage.");
        }
        var directory = DirectoryDatabase.GetCollection<BsonDocument>("character_directory");
        var unknown = new BsonDocument("RealmId", new BsonDocument("$nin", new BsonArray(Routes.Select(route => route.RealmId))));
        if (await directory.Find(unknown).AnyAsync(ct)) throw new RealmStorageUnavailableException();
    }
}
