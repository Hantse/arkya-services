using System.Security.Cryptography;
using System.Text;
using Arkya.Characters.Api.Application;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Arkya.Characters.Api.Infrastructure.Persistence;

public sealed record RealmMigrationReport(long Characters, long Leases, long Quests, string SourceFingerprint, bool Applied);

// Offline only: the operator must stop every legacy writer before applying this copy.
public sealed class RealmStorageMigration(RealmMongoDatabaseProvider storage)
{
    private static readonly string[] Collections = ["characters", "game_leases", "character_quests"];

    public async Task<RealmMigrationReport> RunAsync(bool apply, bool writersStopped, string? unassignedRealm, CancellationToken ct)
    {
        if (storage.Enabled || storage.Routes.Count == 0 || apply && !writersStopped)
            throw new InvalidOperationException("Migration requires configured routes, RealmStorage:Enabled=false and explicit stopped-writers acknowledgement for apply.");
        if (unassignedRealm is not null) _ = storage.GetRealmDatabase(unassignedRealm);
        var legacy = storage.LegacyDatabase;
        var now = DateTime.UtcNow;
        var active = new BsonDocument("$or", new BsonArray
        {
            new BsonDocument("LeaseExpiresAt", new BsonDocument("$gt", now)),
            new BsonDocument("TicketExpiresAt", new BsonDocument("$gt", now)),
            new BsonDocument("TransferExpiresAt", new BsonDocument("$gt", now))
        });
        if (await legacy.GetCollection<BsonDocument>("game_leases").Find(active).AnyAsync(ct))
            throw new InvalidOperationException("Release or expire all legacy leases, tickets and transfers before migration.");

        var fingerprint = await FingerprintAsync(ct);
        var directory = storage.DirectoryDatabase.GetCollection<BsonDocument>("character_directory")
            .WithWriteConcern(WriteConcern.WMajority.With(journal: true));
        var markers = storage.DirectoryDatabase.GetCollection<BsonDocument>("realm_storage_migrations")
            .WithWriteConcern(WriteConcern.WMajority.With(journal: true));
        var completed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var route in storage.Routes)
        {
            var marker = await markers.Find(new BsonDocument("_id", route.RealmId)).FirstOrDefaultAsync(ct);
            if (marker is not null && marker.GetValue("SourceFingerprint", "") != fingerprint)
                throw new InvalidOperationException("The legacy source differs from an earlier migration. Resolve the source change explicitly before retrying.");
            if (marker?.GetValue("Status", "") == "Complete")
            {
                if (marker.GetValue("RouteFingerprint", "") != RealmMongoDatabaseProvider.RouteFingerprint(route))
                    throw new InvalidOperationException("A completed realm migration points at a different destination.");
                completed.Add(route.RealmId);
            }
        }
        await ValidateExistingTargetsAsync(completed, unassignedRealm, ct);
        if (apply)
        {
            await directory.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(new BsonDocument("NormalizedName", 1),
                new CreateIndexOptions { Name = "ux_directory_normalized_name", Unique = true }), cancellationToken: ct);
            foreach (var route in storage.Routes.Where(route => !completed.Contains(route.RealmId)))
                await markers.ReplaceOneAsync(new BsonDocument("_id", route.RealmId), new BsonDocument
                {
                    ["_id"] = route.RealmId, ["DatabaseName"] = route.DatabaseName, ["Status"] = "Copying",
                    ["RouteFingerprint"] = RealmMongoDatabaseProvider.RouteFingerprint(route), ["SourceFingerprint"] = fingerprint
                }, new ReplaceOptions { IsUpsert = true }, ct);
        }

        long characters = 0, leases = 0, quests = 0;
        var counts = storage.Routes.ToDictionary(route => route.RealmId, _ => new long[3], StringComparer.Ordinal);
        using var cursor = await legacy.GetCollection<BsonDocument>("characters")
            .Find(FilterDefinition<BsonDocument>.Empty).Sort(new BsonDocument("_id", 1)).ToCursorAsync(ct);
        while (await cursor.MoveNextAsync(ct))
        foreach (var source in cursor.Current)
        {
            var character = source.DeepClone().AsBsonDocument;
            var id = character["_id"].AsString;
            var realmId = character.GetValue("RealmId", BsonNull.Value).IsString ? character["RealmId"].AsString : unassignedRealm;
            if (realmId is null) throw new InvalidOperationException("Legacy characters without a realm require an explicit unassigned-realm policy.");
            var target = storage.GetRealmDatabase(realmId);
            character["RealmId"] = realmId;
            var reference = new BsonDocument
            {
                ["_id"] = id, ["AccountId"] = character["AccountId"], ["RealmId"] = realmId,
                ["NormalizedName"] = character["NormalizedName"], ["Status"] = "Ready"
            };
            var previous = await directory.Find(new BsonDocument("_id", id)).FirstOrDefaultAsync(ct);
            if (previous is not null && !previous.Equals(reference))
                throw new InvalidOperationException("The character directory conflicts with the migration source.");
            if (!completed.Contains(realmId))
            {
                await CopyExactAsync(target, "characters", character, apply, ct);
                await CopyExactAsync(storage.DirectoryDatabase, "character_directory", reference, apply, ct);
            }
            characters++;
            counts[realmId][0]++;

            var lease = await legacy.GetCollection<BsonDocument>("game_leases").Find(new BsonDocument("_id", id)).FirstOrDefaultAsync(ct);
            if (lease is not null)
            {
                if (lease["AccountId"] != character["AccountId"] || lease["RealmId"] != realmId)
                    throw new InvalidOperationException("A legacy lease does not match its character owner or realm.");
                if (!completed.Contains(realmId)) await CopyExactAsync(target, "game_leases", lease, apply, ct);
                leases++;
                counts[realmId][1]++;
            }
            using var questCursor = await legacy.GetCollection<BsonDocument>("character_quests")
                .Find(new BsonDocument("CharacterId", id)).ToCursorAsync(ct);
            while (await questCursor.MoveNextAsync(ct))
            foreach (var quest in questCursor.Current)
            {
                if (!completed.Contains(realmId)) await CopyExactAsync(target, "character_quests", quest, apply, ct);
                quests++;
                counts[realmId][2]++;
            }
        }
        var allLeases = await legacy.GetCollection<BsonDocument>("game_leases").CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty, cancellationToken: ct);
        var allQuests = await legacy.GetCollection<BsonDocument>("character_quests").CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty, cancellationToken: ct);
        if (allLeases != leases || allQuests != quests)
            throw new InvalidOperationException("Legacy gameplay contains orphaned leases or quests; resolve them before migration.");
        if (fingerprint != await FingerprintAsync(ct))
            throw new InvalidOperationException("Legacy data changed during migration. Keep isolated mode disabled and retry with all writers stopped.");
        if (apply)
        {
            foreach (var route in storage.Routes.Where(route => !completed.Contains(route.RealmId)))
            {
                // An unmapped target row would otherwise appear in administrative totals after cutover.
                var target = storage.GetRealmDatabase(route.RealmId);
                for (var index = 0; index < Collections.Length; index++)
                    if (await target.GetCollection<BsonDocument>(Collections[index])
                        .CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty, cancellationToken: ct) != counts[route.RealmId][index])
                        throw new InvalidOperationException("The target contains unexpected gameplay documents; migration was not activated.");
                if (await directory.CountDocumentsAsync(new BsonDocument("RealmId", route.RealmId), cancellationToken: ct) != counts[route.RealmId][0])
                    throw new InvalidOperationException("The directory contains unexpected characters for this realm.");
                using var targetCursor = await target.GetCollection<BsonDocument>("characters")
                    .Find(FilterDefinition<BsonDocument>.Empty).ToCursorAsync(ct);
                while (await targetCursor.MoveNextAsync(ct))
                foreach (var row in targetCursor.Current)
                {
                    var reference = await directory.Find(new BsonDocument
                    {
                        ["_id"] = row["_id"], ["AccountId"] = row["AccountId"], ["RealmId"] = route.RealmId, ["Status"] = "Ready"
                    }).AnyAsync(ct);
                    if (!reference || row.GetValue("RealmId", "") != route.RealmId)
                        throw new InvalidOperationException("The target contains an unmapped character.");
                }
                await markers.UpdateOneAsync(new BsonDocument("_id", route.RealmId), new BsonDocument("$set", new BsonDocument
                {
                    ["Status"] = "Complete", ["SourceFingerprint"] = fingerprint, ["CompletedAt"] = DateTime.UtcNow
                }), cancellationToken: ct);
            }
        }
        return new(characters, leases, quests, fingerprint, apply);
    }

    private static async Task CopyExactAsync(IMongoDatabase database, string name, BsonDocument document, bool apply, CancellationToken ct)
    {
        var collection = database.GetCollection<BsonDocument>(name).WithWriteConcern(WriteConcern.WMajority.With(journal: true));
        var filter = new BsonDocument("_id", document["_id"]);
        var existing = await collection.Find(filter).FirstOrDefaultAsync(ct);
        if (existing is not null)
        {
            if (!existing.Equals(document)) throw new InvalidOperationException("A migration target document differs from the source; no overwrite was performed.");
            return;
        }
        if (!apply) return;
        try { await collection.InsertOneAsync(document, cancellationToken: ct); }
        catch (MongoWriteException error) when (error.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            existing = await collection.Find(filter).FirstOrDefaultAsync(ct);
            if (existing is null || !existing.Equals(document)) throw new InvalidOperationException("A migration target conflicts with the source.");
        }
    }

    private async Task<string> FingerprintAsync(CancellationToken ct)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var name in Collections)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(name));
            using var cursor = await storage.LegacyDatabase.GetCollection<BsonDocument>(name)
                .Find(FilterDefinition<BsonDocument>.Empty).Sort(new BsonDocument("_id", 1)).ToCursorAsync(ct);
            while (await cursor.MoveNextAsync(ct))
                foreach (var row in cursor.Current) hash.AppendData(row.ToBson());
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private async Task ValidateExistingTargetsAsync(HashSet<string> completed, string? unassignedRealm, CancellationToken ct)
    {
        foreach (var route in storage.Routes.Where(route => !completed.Contains(route.RealmId)))
        foreach (var name in Collections.Append("character_directory"))
        {
            var target = name == "character_directory" ? storage.DirectoryDatabase : storage.GetRealmDatabase(route.RealmId);
            var filter = name == "character_directory" ? new BsonDocument("RealmId", route.RealmId) : new BsonDocument();
            using var cursor = await target.GetCollection<BsonDocument>(name).Find(filter).ToCursorAsync(ct);
            while (await cursor.MoveNextAsync(ct))
            foreach (var row in cursor.Current)
            {
                var characterId = name == "character_quests" ? row["CharacterId"] : row["_id"];
                var character = await storage.LegacyDatabase.GetCollection<BsonDocument>("characters")
                    .Find(new BsonDocument("_id", characterId)).FirstOrDefaultAsync(ct);
                var realm = character?.GetValue("RealmId", BsonNull.Value);
                var assigned = realm?.IsString == true ? realm.AsString : unassignedRealm;
                if (character is null || assigned != route.RealmId)
                    throw new InvalidOperationException("The target contains a document outside the migration source.");
                BsonDocument? expected;
                if (name == "character_directory") expected = new BsonDocument
                {
                    ["_id"] = character["_id"], ["AccountId"] = character["AccountId"], ["RealmId"] = route.RealmId,
                    ["NormalizedName"] = character["NormalizedName"], ["Status"] = "Ready"
                };
                else
                {
                    expected = await storage.LegacyDatabase.GetCollection<BsonDocument>(name)
                        .Find(new BsonDocument("_id", row["_id"])).FirstOrDefaultAsync(ct);
                    if (name == "characters" && expected is not null) expected["RealmId"] = route.RealmId;
                }
                if (expected is null || !expected.Equals(row))
                    throw new InvalidOperationException("A migration target differs from its legacy source.");
            }
        }
    }
}
