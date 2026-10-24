using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;
using Arkya.Characters.Api.Application;
using Arkya.ServiceDefaults;
using Arkya.ServiceDefaults.Characters;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Arkya.Characters.Api.Infrastructure.Persistence;

public sealed class MongoCharacterAdminRepository : ICharacterAdminRepository
{
    private const int CursorBatchSize = 128;
    private readonly IMongoDatabase legacyDatabase;
    private readonly RealmMongoDatabaseProvider? databases;

    public MongoCharacterAdminRepository(IMongoClient client, IOptions<MongoOptions> options) =>
        legacyDatabase = client.GetDatabase(options.Value.DatabaseName);

    public MongoCharacterAdminRepository(RealmMongoDatabaseProvider databases)
    {
        this.databases = databases;
        legacyDatabase = databases.LegacyDatabase;
    }

    public async Task<AdminCharacterSummary?> FindAsync(Guid characterId, string? realmId, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var source = await ResolveCharacterAsync(characterId, realmId, cancellationToken);
        if (source is null) return null;
        var filter = new BsonDocument("_id", characterId.ToString("D"));
        if (realmId is not null) filter["RealmId"] = realmId;
        if (source.Value.Directory is { } directory)
        {
            filter["AccountId"] = directory["AccountId"];
            filter["RealmId"] = directory["RealmId"];
        }
        BsonDocument[] pipeline = [new("$match", filter), CharacterProjection(), ActiveLeaseLookup(now)];
        var document = await source.Value.Database.GetCollection<BsonDocument>("characters").Aggregate<BsonDocument>(pipeline,
            new AggregateOptions { MaxTime = TimeSpan.FromSeconds(5) }).SingleOrDefaultAsync(cancellationToken);
        return document is null ? null : ToSummary(document);
    }

    public async Task<AdminCharacterQuestPage> ListQuestsAsync(Guid characterId, int page, int pageSize,
        CancellationToken cancellationToken)
    {
        if (page is < 1 or > 100000 || pageSize is < 1 or > 100)
            throw new ArgumentException("Invalid character quest page.");
        var source = await ResolveCharacterAsync(characterId, null, cancellationToken);
        if (source is null) return new([], 0, page, pageSize);
        if (source.Value.Directory is { } directory && !await source.Value.Database.GetCollection<BsonDocument>("characters")
            .Find(new BsonDocument { ["_id"] = directory["_id"], ["AccountId"] = directory["AccountId"],
                ["RealmId"] = directory["RealmId"] }).AnyAsync(cancellationToken))
            return new([], 0, page, pageSize);
        BsonDocument[] pipeline =
        [
            new("$match", new BsonDocument("CharacterId", characterId.ToString("D"))),
            new("$facet", new BsonDocument
            {
                ["Total"] = new BsonArray { new BsonDocument("$count", "Value") },
                ["Items"] = new BsonArray
                {
                    new BsonDocument("$sort", new BsonDocument("QuestId", 1)),
                    new BsonDocument("$skip", (page - 1) * pageSize), new BsonDocument("$limit", pageSize),
                    new BsonDocument("$project", new BsonDocument
                    {
                        ["_id"] = 0, ["QuestId"] = 1, ["CatalogVersion"] = 1,
                        ["RequiredKills"] = 1, ["Kills"] = 1, ["Revision"] = 1
                    })
                }
            })
        ];
        var result = await source.Value.Database.GetCollection<CharacterQuestDocument>("character_quests").Aggregate<BsonDocument>(pipeline,
            new AggregateOptions { MaxTime = TimeSpan.FromSeconds(5) }).SingleAsync(cancellationToken);
        var totals = result["Total"].AsBsonArray;
        return new(result["Items"].AsBsonArray.Select(value => new AdminCharacterQuest(value["QuestId"].AsString,
            Guid.Parse(value["CatalogVersion"].AsString), value["RequiredKills"].AsInt32, value["Kills"].AsInt32,
            value["Kills"].AsInt32 == value["RequiredKills"].AsInt32, value["Revision"].ToInt64())).ToArray(),
            totals.Count == 0 ? 0 : totals[0]["Value"].ToInt64(), page, pageSize);
    }

    public async Task<AdminCharacterPage> ListAsync(CharacterAdminQuery query, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!query.IsValid()) throw new ArgumentException("Invalid character query.", nameof(query));
        if (databases?.Enabled == true) return await ListRealmsAsync(query, now, cancellationToken);
        var pipeline = CharacterPipeline(query, now);
        pipeline.Add(new("$facet", new BsonDocument
        {
            ["Total"] = new BsonArray { new BsonDocument("$count", "Value") },
            ["Items"] = new BsonArray
            {
                new BsonDocument("$sort", CharacterSort()),
                new BsonDocument("$skip", (query.Page - 1) * query.PageSize), new BsonDocument("$limit", query.PageSize)
            }
        }));
        var result = await legacyDatabase.GetCollection<BsonDocument>("characters").Aggregate<BsonDocument>(pipeline,
            new AggregateOptions { MaxTime = TimeSpan.FromSeconds(5) }).SingleAsync(cancellationToken);
        var totals = result["Total"].AsBsonArray;
        return new(result["Items"].AsBsonArray.Select(value => ToSummary(value.AsBsonDocument)).ToArray(),
            totals.Count == 0 ? 0 : totals[0]["Value"].ToInt64(), query.Page, query.PageSize);
    }

    private static List<BsonDocument> CharacterPipeline(CharacterAdminQuery query, DateTimeOffset now)
    {
        var pipeline = new List<BsonDocument>();
        var filter = new BsonDocument();
        if (query.AccountId is { } accountId) filter["AccountId"] = accountId.ToString("D");
        if (query.RealmId is { } realmId) filter["RealmId"] = realmId;
        if (query.Search.Length > 0)
        {
            var literal = new BsonRegularExpression(Regex.Escape(query.Search), "i");
            filter["$or"] = new BsonArray { new BsonDocument("Name", literal), new BsonDocument("_id", literal) };
        }
        if (filter.ElementCount > 0) pipeline.Add(new("$match", filter));
        pipeline.Add(CharacterProjection());
        pipeline.Add(ActiveLeaseLookup(now));
        if (query.OnlineOnly) pipeline.Add(new("$match", new BsonDocument("ActiveLease.0", new BsonDocument("$exists", true))));
        return pipeline;
    }

    public async Task<AdminCharacterOverview> OverviewAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (databases?.Enabled == true)
        {
            long total = 0;
            var realmCounts = new List<RealmOnlineCount>();
            foreach (var route in databases.Routes)
            {
                long online = 0;
                await foreach (var character in ReadRealmAsync(route.RealmId,
                    new(null, "", false, 1, 1, route.RealmId), now, cancellationToken))
                {
                    total++;
                    if (character["ActiveLease"].AsBsonArray.Count > 0) online++;
                }
                if (online > 0) realmCounts.Add(new(route.RealmId, online));
            }
            return new(total, realmCounts.Sum(value => value.OnlineCharacters),
                realmCounts.OrderBy(value => value.RealmId, StringComparer.Ordinal).ToArray());
        }
        BsonDocument[] pipeline =
        [
            new("$project", new BsonDocument { ["_id"] = 1, ["AccountId"] = 1, ["RealmId"] = 1 }),
            ActiveLeaseLookup(now),
            new("$facet", new BsonDocument
            {
                ["Total"] = new BsonArray { new BsonDocument("$count", "Value") },
                ["Realms"] = new BsonArray
                {
                    new BsonDocument("$match", new BsonDocument("ActiveLease.0", new BsonDocument("$exists", true))),
                    new BsonDocument("$group", new BsonDocument
                    {
                        ["_id"] = new BsonDocument("$ifNull", new BsonArray { "$RealmId", "" }),
                        ["OnlineCharacters"] = new BsonDocument("$sum", 1)
                    }),
                    new BsonDocument("$sort", new BsonDocument("_id", 1))
                }
            })
        ];
        var result = await legacyDatabase.GetCollection<BsonDocument>("characters").Aggregate<BsonDocument>(pipeline, new AggregateOptions { MaxTime = TimeSpan.FromSeconds(5) })
            .SingleAsync(cancellationToken);
        var totals = result["Total"].AsBsonArray;
        var realms = result["Realms"].AsBsonArray.Select(value => new RealmOnlineCount(
            value["_id"].AsString, value["OnlineCharacters"].ToInt64())).ToArray();
        return new(totals.Count == 0 ? 0 : totals[0]["Value"].ToInt64(), realms.Sum(value => value.OnlineCharacters), realms);
    }

    private async Task<(IMongoDatabase Database, BsonDocument? Directory)?> ResolveCharacterAsync(Guid characterId,
        string? realmId, CancellationToken cancellationToken)
    {
        if (databases?.Enabled != true) return (legacyDatabase, null);
        var directory = await databases.DirectoryDatabase.GetCollection<BsonDocument>("character_directory")
            .Find(new BsonDocument { ["_id"] = characterId.ToString("D"), ["Status"] = "Ready" })
            .Project<BsonDocument>(new BsonDocument { ["_id"] = 1, ["AccountId"] = 1, ["RealmId"] = 1 })
            .FirstOrDefaultAsync(cancellationToken);
        if (directory is null || realmId is not null && directory.GetValue("RealmId", BsonNull.Value) != realmId) return null;
        var database = await databases.ResolveCharacterDatabaseAsync(characterId, cancellationToken);
        return database is null ? null : (database, directory);
    }

    private async Task<AdminCharacterPage> ListRealmsAsync(CharacterAdminQuery query, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var routes = databases!.Routes.Where(route => query.RealmId is null || route.RealmId == query.RealmId);
        var readers = new List<IAsyncEnumerator<BsonDocument>>();
        var ready = new PriorityQueue<(IAsyncEnumerator<BsonDocument> Reader, BsonDocument Document), CharacterOrder>(CharacterOrderComparer.Instance);
        var page = new List<AdminCharacterSummary>(query.PageSize);
        var offset = (long)(query.Page - 1) * query.PageSize;
        long total = 0;
        try
        {
            foreach (var route in routes)
            {
                var reader = ReadRealmAsync(route.RealmId, query with { RealmId = route.RealmId }, now, cancellationToken)
                    .GetAsyncEnumerator(cancellationToken);
                readers.Add(reader);
                if (await reader.MoveNextAsync()) ready.Enqueue((reader, reader.Current), Order(reader.Current));
            }
            // Count the filtered stream while retaining only the requested global page.
            while (ready.TryDequeue(out var next, out _))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (total >= offset && page.Count < query.PageSize) page.Add(ToSummary(next.Document));
                total++;
                if (await next.Reader.MoveNextAsync()) ready.Enqueue((next.Reader, next.Reader.Current), Order(next.Reader.Current));
            }
            return new(page.ToArray(), total, query.Page, query.PageSize);
        }
        finally
        {
            foreach (var reader in readers) await reader.DisposeAsync();
        }
    }

    private async IAsyncEnumerable<BsonDocument> ReadRealmAsync(string realmId, CharacterAdminQuery query,
        DateTimeOffset now, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var pipeline = CharacterPipeline(query, now);
        pipeline.Add(new("$sort", CharacterSort()));
        using var cursor = await databases!.GetRealmDatabase(realmId).GetCollection<BsonDocument>("characters")
            .AggregateAsync<BsonDocument>(pipeline, new AggregateOptions { MaxTime = TimeSpan.FromSeconds(5), BatchSize = CursorBatchSize }, cancellationToken);
        while (await cursor.MoveNextAsync(cancellationToken))
        {
            foreach (var batch in cursor.Current.Chunk(CursorBatchSize))
            {
                var filter = new BsonDocument
                {
                    ["_id"] = new BsonDocument("$in", new BsonArray(batch.Select(value => value["_id"]))),
                    ["RealmId"] = realmId, ["Status"] = "Ready"
                };
                var canonical = await databases.DirectoryDatabase.GetCollection<BsonDocument>("character_directory").Find(filter)
                    .Project<BsonDocument>(new BsonDocument { ["_id"] = 1, ["AccountId"] = 1 }).ToListAsync(cancellationToken);
                var owners = canonical.ToDictionary(value => value["_id"].AsString, value => value["AccountId"]);
                foreach (var character in batch)
                    if (owners.TryGetValue(character["_id"].AsString, out var owner) && owner == character["AccountId"])
                        yield return character;
            }
        }
    }

    private static BsonDocument CharacterSort() => new() { ["CreatedAt"] = -1, ["_id"] = 1 };
    private static CharacterOrder Order(BsonDocument character) => new(character["CreatedAt"].ToUniversalTime(), character["_id"].AsString);
    private sealed record CharacterOrder(DateTime CreatedAt, string CharacterId);
    private sealed class CharacterOrderComparer : IComparer<CharacterOrder>
    {
        public static readonly CharacterOrderComparer Instance = new();
        public int Compare(CharacterOrder? left, CharacterOrder? right)
        {
            var byDate = right!.CreatedAt.CompareTo(left!.CreatedAt);
            return byDate != 0 ? byDate : StringComparer.Ordinal.Compare(left.CharacterId, right.CharacterId);
        }
    }

    private static BsonDocument CharacterProjection() => new("$project", new BsonDocument
    {
        ["_id"] = 1, ["AccountId"] = 1, ["Name"] = 1, ["RealmId"] = 1, ["CreatedAt"] = 1,
        ["Appearance.Race"] = 1, ["Appearance.Silhouette"] = 1
    });

    private static BsonDocument ActiveLeaseLookup(DateTimeOffset now) => new("$lookup", new BsonDocument
    {
        ["from"] = "game_leases", ["let"] = new BsonDocument
        {
            ["characterId"] = "$_id", ["accountId"] = "$AccountId",
            ["realmId"] = new BsonDocument("$ifNull", new BsonArray { "$RealmId", BsonNull.Value })
        },
        ["pipeline"] = new BsonArray
        {
            new BsonDocument("$match", new BsonDocument("$expr", new BsonDocument("$and", new BsonArray
            {
                new BsonDocument("$eq", new BsonArray { "$_id", "$$characterId" }),
                new BsonDocument("$eq", new BsonArray { "$AccountId", "$$accountId" }),
                new BsonDocument("$eq", new BsonArray { new BsonDocument("$ifNull", new BsonArray { "$RealmId", BsonNull.Value }), "$$realmId" }),
                new BsonDocument("$ne", new BsonArray { "$LeaseId", BsonNull.Value }),
                new BsonDocument("$ne", new BsonArray { "$LeaseId", "" }),
                new BsonDocument("$ne", new BsonArray { "$LeaseId", Guid.Empty.ToString("D") }),
                new BsonDocument("$gt", new BsonArray { "$AuthorityEpoch", 0 }),
                new BsonDocument("$gt", new BsonArray { "$LeaseExpiresAt", new BsonDateTime(now.UtcDateTime) }),
                new BsonDocument("$gt", new BsonArray { "$SessionExpiresAt", new BsonDateTime(now.UtcDateTime) })
            }))),
            new BsonDocument("$project", new BsonDocument { ["_id"] = 0, ["LeaseExpiresAt"] = 1 })
        },
        ["as"] = "ActiveLease"
    });

    private static AdminCharacterSummary ToSummary(BsonDocument document)
    {
        var appearance = document.GetValue("Appearance", BsonNull.Value) as BsonDocument;
        var leases = document["ActiveLease"].AsBsonArray;
        return new(document["_id"].AsString, document["AccountId"].AsString, document["Name"].AsString,
            document.GetValue("RealmId", BsonNull.Value).IsString ? document["RealmId"].AsString : "",
            appearance?.GetValue("Race", "human").AsString ?? "human",
            appearance?.GetValue("Silhouette", "a").AsString ?? "a",
            new DateTimeOffset(document["CreatedAt"].ToUniversalTime()), leases.Count > 0,
            leases.Count > 0 ? new DateTimeOffset(leases[0]["LeaseExpiresAt"].ToUniversalTime()) : null);
    }
}
