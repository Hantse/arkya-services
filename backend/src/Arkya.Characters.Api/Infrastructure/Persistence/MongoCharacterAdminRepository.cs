using System.Text.RegularExpressions;
using Arkya.Characters.Api.Application;
using Arkya.ServiceDefaults;
using Arkya.ServiceDefaults.Characters;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Arkya.Characters.Api.Infrastructure.Persistence;

public sealed class MongoCharacterAdminRepository(IMongoClient client, IOptions<MongoOptions> options) : ICharacterAdminRepository
{
    private readonly IMongoCollection<BsonDocument> characters = client.GetDatabase(options.Value.DatabaseName)
        .GetCollection<BsonDocument>("characters");
    private readonly IMongoCollection<CharacterQuestDocument> quests = client.GetDatabase(options.Value.DatabaseName)
        .GetCollection<CharacterQuestDocument>("character_quests");

    public async Task<AdminCharacterSummary?> FindAsync(Guid characterId, string? realmId, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var filter = new BsonDocument("_id", characterId.ToString("D"));
        if (realmId is not null) filter["RealmId"] = realmId;
        BsonDocument[] pipeline = [new("$match", filter), CharacterProjection(), ActiveLeaseLookup(now)];
        var document = await characters.Aggregate<BsonDocument>(pipeline,
            new AggregateOptions { MaxTime = TimeSpan.FromSeconds(5) }).SingleOrDefaultAsync(cancellationToken);
        return document is null ? null : ToSummary(document);
    }

    public async Task<AdminCharacterQuestPage> ListQuestsAsync(Guid characterId, int page, int pageSize,
        CancellationToken cancellationToken)
    {
        if (page is < 1 or > 100000 || pageSize is < 1 or > 100)
            throw new ArgumentException("Invalid character quest page.");
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
        var result = await quests.Aggregate<BsonDocument>(pipeline,
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
        pipeline.Add(new("$facet", new BsonDocument
        {
            ["Total"] = new BsonArray { new BsonDocument("$count", "Value") },
            ["Items"] = new BsonArray
            {
                new BsonDocument("$sort", new BsonDocument { ["CreatedAt"] = -1, ["_id"] = 1 }),
                new BsonDocument("$skip", (query.Page - 1) * query.PageSize), new BsonDocument("$limit", query.PageSize)
            }
        }));
        var result = await characters.Aggregate<BsonDocument>(pipeline, new AggregateOptions { MaxTime = TimeSpan.FromSeconds(5) })
            .SingleAsync(cancellationToken);
        var totals = result["Total"].AsBsonArray;
        return new(result["Items"].AsBsonArray.Select(value => ToSummary(value.AsBsonDocument)).ToArray(),
            totals.Count == 0 ? 0 : totals[0]["Value"].ToInt64(), query.Page, query.PageSize);
    }

    public async Task<AdminCharacterOverview> OverviewAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        BsonDocument[] pipeline =
        [
            new("$project", new BsonDocument { ["_id"] = 1, ["RealmId"] = 1 }),
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
        var result = await characters.Aggregate<BsonDocument>(pipeline, new AggregateOptions { MaxTime = TimeSpan.FromSeconds(5) })
            .SingleAsync(cancellationToken);
        var totals = result["Total"].AsBsonArray;
        var realms = result["Realms"].AsBsonArray.Select(value => new RealmOnlineCount(
            value["_id"].AsString, value["OnlineCharacters"].ToInt64())).ToArray();
        return new(totals.Count == 0 ? 0 : totals[0]["Value"].ToInt64(), realms.Sum(value => value.OnlineCharacters), realms);
    }

    private static BsonDocument CharacterProjection() => new("$project", new BsonDocument
    {
        ["_id"] = 1, ["AccountId"] = 1, ["Name"] = 1, ["RealmId"] = 1, ["CreatedAt"] = 1,
        ["Appearance.Race"] = 1, ["Appearance.Silhouette"] = 1
    });

    private static BsonDocument ActiveLeaseLookup(DateTimeOffset now) => new("$lookup", new BsonDocument
    {
        ["from"] = "game_leases", ["let"] = new BsonDocument("characterId", "$_id"),
        ["pipeline"] = new BsonArray
        {
            new BsonDocument("$match", new BsonDocument("$expr", new BsonDocument("$and", new BsonArray
            {
                new BsonDocument("$eq", new BsonArray { "$_id", "$$characterId" }),
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
