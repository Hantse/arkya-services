using System.Text.Json;
using Arkya.Characters.Api.Application;
using Arkya.Characters.Api.Domain;
using Arkya.Characters.Api.Infrastructure.Persistence;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Arkya.Characters.Tests;

public sealed class MongoCharacterAdminRepositoryTests
{
    [MongoFact]
    public async Task ListsProjectedCharactersWithAccuratePaginationAndOnlyActiveLeasesCountAsOnline()
    {
        var database = "arkya_character_admin_test_" + Guid.NewGuid().ToString("N");
        var client = new MongoClient(Environment.GetEnvironmentVariable("ARKYA_TEST_MONGO"));
        try
        {
            var options = Options.Create(new MongoOptions { DatabaseName = database });
            var characters = new MongoCharacterRepository(client, options);
            var leases = new MongoGameLeaseStore(client, options);
            var admin = new MongoCharacterAdminRepository(client, options);
            await characters.EnsureIndexesAsync(default);
            await leases.EnsureIndexesAsync(default);
            var owner = Guid.NewGuid();
            var now = FixedTimeProvider.Now;
            var names = new[] { "Active", "Pending", "Expired", "Sessionexpired", "Released", "NoLease", "Other" };
            var all = names.Select((name, index) => new Character(Guid.NewGuid(), index == 6 ? Guid.NewGuid() : owner,
                name, name.ToUpperInvariant(), 1, now.AddSeconds(index)) { RealmId = index == 6 ? "other-realm" : "beta-web" }).ToArray();
            foreach (var character in all) Assert.True(await characters.TryAddAsync(character, default));
            for (var index = 0; index < all.Length; index++)
            {
                if (index == 5) continue;
                var character = all[index];
                var record = new GameLeaseRecord(character.Id, character.AccountId, character.RealmId!, "starter-island", 1,
                    index == 1 ? "private-admission-ticket-hash" : null, now.AddSeconds(60),
                    index is 1 or 4 ? Guid.Empty : Guid.NewGuid(), 1,
                    index == 2 ? now : now.AddSeconds(90), index == 3 ? now : now.AddHours(1), null,
                    "private-session-reference");
                Assert.True(await leases.TryWriteAsync(record, null, default));
            }
            var overview = await admin.OverviewAsync(now, default);
            Assert.Equal(7, overview.TotalCharacters);
            Assert.Equal(2, overview.OnlineCharacters);
            Assert.Collection(overview.Realms, realm => { Assert.Equal("beta-web", realm.RealmId); Assert.Equal(1, realm.OnlineCharacters); },
                realm => { Assert.Equal("other-realm", realm.RealmId); Assert.Equal(1, realm.OnlineCharacters); });

            var online = await admin.ListAsync(new(null, "", true, 1, 1), now, default);
            Assert.Equal(2, online.Total);
            var first = Assert.Single(online.Items);
            Assert.Equal("Other", first.DisplayName);
            Assert.True(first.IsOnline);
            Assert.Equal(now.AddSeconds(90), first.LeaseExpiresAt);
            Assert.Equal("Active", Assert.Single((await admin.ListAsync(new(null, "", true, 2, 1), now, default)).Items).DisplayName);
            var owned = await admin.ListAsync(new(owner, "", false, 1, 25), now, default);
            Assert.Equal(6, owned.Total);
            Assert.All(owned.Items, value => Assert.Equal(owner.ToString("D"), value.AccountId));
            Assert.Equal(1, (await admin.ListAsync(new(owner, "aCt", true, 1, 25), now, default)).Total);
            Assert.Empty((await admin.ListAsync(new(null, ".*", false, 1, 25), now, default)).Items);
            Assert.Equal(all[0].Id.ToString("D"), Assert.Single((await admin.ListAsync(new(null, all[0].Id.ToString("D"), false, 1, 25), now, default)).Items).CharacterId);
            var serialized = JsonSerializer.Serialize(owned);
            Assert.DoesNotContain("private-", serialized, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("LeaseId", serialized, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("SessionReference", serialized, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("TicketHash", serialized, StringComparison.OrdinalIgnoreCase);
            Assert.All(owned.Items.Where(value => !value.IsOnline), value => Assert.Null(value.LeaseExpiresAt));

            var legacy = CharacterDocument.From(new Character(Guid.NewGuid(), owner, "Legacy", "LEGACY", 1, now)).ToBsonDocument();
            legacy["SchemaVersion"] = 1;
            legacy.Remove("Appearance");
            await client.GetDatabase(database).GetCollection<BsonDocument>("characters").InsertOneAsync(legacy);
            var legacySummary = Assert.Single((await admin.ListAsync(new(null, "Legacy", false, 1, 25), now, default)).Items);
            Assert.Equal("human", legacySummary.Race);
            Assert.Equal("a", legacySummary.Gender);
            Assert.Equal("", legacySummary.RealmId);
            Assert.False(legacySummary.IsOnline);
            Assert.Equal(0, (await admin.OverviewAsync(now.AddHours(1), default)).OnlineCharacters);
        }
        finally { await client.DropDatabaseAsync(database); }
    }

    [MongoFact]
    public async Task RealmFilterKeepsTotalsPaginationAndCombinedFiltersWithinTheSelectedRealm()
    {
        var database = "arkya_character_admin_test_" + Guid.NewGuid().ToString("N");
        var client = new MongoClient(Environment.GetEnvironmentVariable("ARKYA_TEST_MONGO"));
        try
        {
            var options = Options.Create(new MongoOptions { DatabaseName = database });
            var characters = new MongoCharacterRepository(client, options);
            var leases = new MongoGameLeaseStore(client, options);
            var admin = new MongoCharacterAdminRepository(client, options);
            await characters.EnsureIndexesAsync(default);
            await leases.EnsureIndexesAsync(default);
            var owner = Guid.NewGuid();
            var otherOwner = Guid.NewGuid();
            var now = FixedTimeProvider.Now;
            var fixtures = new[]
            {
                (Name: "AelysOld", Realm: (string?)"lerya", Owner: owner, Online: true),
                (Name: "AelysOther", Realm: (string?)"other-realm", Owner: owner, Online: true),
                (Name: "AelysNew", Realm: (string?)"lerya", Owner: owner, Online: false),
                (Name: "AelysGuest", Realm: (string?)"lerya", Owner: otherOwner, Online: true),
                (Name: "AelysLegacy", Realm: (string?)null, Owner: owner, Online: false)
            };
            for (var index = 0; index < fixtures.Length; index++)
            {
                var fixture = fixtures[index];
                var character = new Character(Guid.NewGuid(), fixture.Owner, fixture.Name, fixture.Name.ToUpperInvariant(),
                    1, now.AddSeconds(index)) { RealmId = fixture.Realm };
                Assert.True(await characters.TryAddAsync(character, default));
                if (!fixture.Online) continue;
                var lease = new GameLeaseRecord(character.Id, fixture.Owner, fixture.Realm!, "island", 1, null,
                    now.AddSeconds(60), Guid.NewGuid(), 1, now.AddSeconds(90), now.AddHours(1), null, "private-session-reference");
                Assert.True(await leases.TryWriteAsync(lease, null, default));
            }

            var firstPage = await admin.ListAsync(new(null, "", false, 1, 1, "lerya"), now, default);
            Assert.Equal(3, firstPage.Total);
            Assert.Equal("AelysGuest", Assert.Single(firstPage.Items).DisplayName);
            var secondPage = await admin.ListAsync(new(null, "", false, 2, 1, "lerya"), now, default);
            Assert.Equal(3, secondPage.Total);
            Assert.Equal("AelysNew", Assert.Single(secondPage.Items).DisplayName);
            var thirdPage = await admin.ListAsync(new(null, "", false, 3, 1, "lerya"), now, default);
            Assert.Equal(3, thirdPage.Total);
            Assert.Equal("AelysOld", Assert.Single(thirdPage.Items).DisplayName);
            Assert.All(firstPage.Items.Concat(secondPage.Items).Concat(thirdPage.Items), value => Assert.Equal("lerya", value.RealmId));
            var beyondLastPage = await admin.ListAsync(new(null, "", false, 4, 1, "lerya"), now, default);
            Assert.Equal(3, beyondLastPage.Total);
            Assert.Empty(beyondLastPage.Items);

            var owned = await admin.ListAsync(new(owner, "aElYs", false, 1, 25, "lerya"), now, default);
            Assert.Equal(2, owned.Total);
            Assert.All(owned.Items, value => Assert.Equal(owner.ToString("D"), value.AccountId));
            var onlineOwned = await admin.ListAsync(new(owner, "aElYs", true, 1, 25, "lerya"), now, default);
            Assert.Equal(1, onlineOwned.Total);
            Assert.Equal("AelysOld", Assert.Single(onlineOwned.Items).DisplayName);
            Assert.Empty((await admin.ListAsync(new(owner, "Guest", false, 1, 25, "lerya"), now, default)).Items);
            Assert.Empty((await admin.ListAsync(new(null, ".*", false, 1, 25, "lerya"), now, default)).Items);
            var missingRealm = await admin.ListAsync(new(null, "", false, 1, 25, "missing-realm"), now, default);
            Assert.Equal(0, missingRealm.Total);
            Assert.Empty(missingRealm.Items);

            var global = await admin.ListAsync(new(null, "", false, 1, 25), now, default);
            Assert.Equal(5, global.Total);
            Assert.Contains(global.Items, value => value.RealmId == "other-realm");
            Assert.Contains(global.Items, value => value.RealmId == "");
        }
        finally { await client.DropDatabaseAsync(database); }
    }

    [MongoFact]
    public async Task EmptyStoreReturnsAnEmptyPageAndZeroOverview()
    {
        var database = "arkya_character_admin_test_" + Guid.NewGuid().ToString("N");
        var client = new MongoClient(Environment.GetEnvironmentVariable("ARKYA_TEST_MONGO"));
        try
        {
            var repository = new MongoCharacterAdminRepository(client, Options.Create(new MongoOptions { DatabaseName = database }));
            var overview = await repository.OverviewAsync(FixedTimeProvider.Now, default);
            Assert.Equal(0, overview.TotalCharacters);
            Assert.Equal(0, overview.OnlineCharacters);
            Assert.Empty(overview.Realms);
            var page = await repository.ListAsync(new(null, "", false, 1, 25), FixedTimeProvider.Now, default);
            Assert.Equal(0, page.Total);
            Assert.Empty(page.Items);
        }
        finally { await client.DropDatabaseAsync(database); }
    }
}
