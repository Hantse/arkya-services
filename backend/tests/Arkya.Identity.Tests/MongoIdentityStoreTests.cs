using Arkya.Identity.Api.Application;
using Arkya.Identity.Api.Domain;
using Arkya.Identity.Api.Infrastructure;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Arkya.Identity.Tests;

public sealed class MongoIdentityStoreTests
{
    [MongoFact]
    public async Task ReturnUrlSurvivesStateClaimsAndLegacyDocumentsRemainReadable()
    {
        await using var database = await TestDatabase.CreateAsync();
        var now = DateTime.UtcNow;
        var challenge = new LoginChallenge(Guid.NewGuid().ToString("N"), IdentityService.CodeChallenge(new string('v', 43)),
            now.AddHours(1), ReturnUrl: "https://arkya.online/account.html?lang=fr&returnTo=play.html");
        await database.Store.CreateChallengeAsync(challenge, CancellationToken.None);
        Assert.Equal(challenge.ReturnUrl, (await database.Store.GetChallengeAsync(challenge.Id, CancellationToken.None))!.ReturnUrl);
        Assert.True(await database.Store.StartBrowserAsync(challenge.Id, "web-state", "web-nonce", now, CancellationToken.None));
        Assert.Equal(challenge.ReturnUrl, (await database.Store.FindStateAsync("web-state", CancellationToken.None))!.ReturnUrl);
        Assert.Equal(challenge.ReturnUrl, (await database.Store.ClaimStateAsync("web-state", "web-nonce", now, CancellationToken.None))!.ReturnUrl);
        await database.Store.CompleteChallengeAsync(challenge.Id, new("account", "123", "Player"), CancellationToken.None);
        Assert.Equal(challenge.ReturnUrl,
            (await database.Store.RedeemChallengeAsync(challenge.Id, challenge.CodeChallenge, now, CancellationToken.None))!.ReturnUrl);

        var legacyId = Guid.NewGuid().ToString("N");
        await database.Collection("login_challenges").InsertOneAsync(new BsonDocument
        {
            ["_id"] = legacyId, ["SchemaVersion"] = 1, ["CodeChallenge"] = challenge.CodeChallenge,
            ["ExpiresAt"] = now.AddHours(1), ["Status"] = (int)ChallengeStatus.Pending
        });
        var legacy = (await database.Store.GetChallengeAsync(legacyId, CancellationToken.None))!;
        Assert.Null(legacy.ReturnUrl);
        Assert.True(await database.Store.StartBrowserAsync(legacyId, "legacy-state", "legacy-nonce", now, CancellationToken.None));
        Assert.Null((await database.Store.ClaimStateAsync("legacy-state", "legacy-nonce", now, CancellationToken.None))!.ReturnUrl);
    }

    [MongoFact]
    public async Task IndexesAndConcurrentFirstLoginsPreserveOneAccountPerDiscordIdentity()
    {
        await using var database = await TestDatabase.CreateAsync();
        var accounts = database.Collection("accounts");
        var accountIndexes = await (await accounts.Indexes.ListAsync()).ToListAsync();
        var discordIndex = Assert.Single(accountIndexes, index => index["name"] == "discord_id_unique");
        Assert.True(discordIndex["unique"].AsBoolean);
        Assert.Equal(1, discordIndex["key"]["DiscordId"].AsInt32);
        var challengeIndexes = await (await database.Collection("login_challenges").Indexes.ListAsync()).ToListAsync();
        var stateIndex = Assert.Single(challengeIndexes, index => index["name"] == "oauth_state_unique");
        Assert.True(stateIndex["unique"].AsBoolean);
        Assert.True(stateIndex.Contains("partialFilterExpression"));
        Assert.Equal(0, Assert.Single(challengeIndexes, index => index["name"] == "challenge_expiry")["expireAfterSeconds"].ToInt32());
        var sessionIndexes = await (await database.Collection("sessions").Indexes.ListAsync()).ToListAsync();
        Assert.Equal(0, Assert.Single(sessionIndexes, index => index["name"] == "session_expiry")["expireAfterSeconds"].ToInt32());

        var now = DateTime.UtcNow;
        var logins = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => database.Store.UpsertAccountAsync("123456789", "Player", now, CancellationToken.None)));
        Assert.Single(logins.Select(account => account.Id).Distinct());
        Assert.Equal(1, await accounts.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));
        var changed = await database.Store.UpsertAccountAsync("123456789", "New Name", now.AddMinutes(1), CancellationToken.None);
        Assert.Equal(logins[0].Id, changed.Id);
        Assert.Equal("New Name", changed.DisplayName);
        Assert.Equal(changed, await database.Store.GetAccountAsync(changed.Id, CancellationToken.None));
        Assert.Null(await database.Store.GetAccountAsync(Guid.NewGuid().ToString(), CancellationToken.None));
        Assert.Equal(2, (await accounts.Find(FilterDefinition<BsonDocument>.Empty).SingleAsync())["SchemaVersion"].AsInt32);
    }

    [MongoFact]
    public async Task ChallengeStateAndRedemptionAreAtomicAndRequireMatchingProofAndExpiry()
    {
        await using var database = await TestDatabase.CreateAsync();
        var now = DateTime.UtcNow;
        var challenge = new LoginChallenge(Guid.NewGuid().ToString("N"), IdentityService.CodeChallenge(new string('v', 43)), now.AddHours(1));
        await database.Store.CreateChallengeAsync(challenge, CancellationToken.None);
        var starts = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => database.Store.StartBrowserAsync(challenge.Id, "state-hash", "nonce-hash", now, CancellationToken.None)));
        Assert.Single(starts, started => started);
        Assert.Null(await database.Store.ClaimStateAsync("state-hash", "wrong-nonce-hash", now, CancellationToken.None));
        var claims = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => database.Store.ClaimStateAsync("state-hash", "nonce-hash", now, CancellationToken.None)));
        Assert.Single(claims, claimed => claimed is not null);
        await database.Store.CompleteChallengeAsync(challenge.Id, new("account-id", "123", "Player"), CancellationToken.None);
        Assert.Null(await database.Store.RedeemChallengeAsync(challenge.Id, "incorrect-proof", now, CancellationToken.None));
        Assert.Null(await database.Store.RedeemChallengeAsync(challenge.Id, challenge.CodeChallenge, challenge.ExpiresAt, CancellationToken.None));
        var redemptions = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => database.Store.RedeemChallengeAsync(challenge.Id, challenge.CodeChallenge, now, CancellationToken.None)));
        var redeemed = Assert.Single(redemptions, value => value is not null)!;
        Assert.Equal("account-id", redeemed.AccountId);
        Assert.Equal(ChallengeStatus.Consumed, (await database.Store.GetChallengeAsync(challenge.Id, CancellationToken.None))!.Status);

        var expired = challenge with { Id = Guid.NewGuid().ToString("N") };
        await database.Store.CreateChallengeAsync(expired, CancellationToken.None);
        Assert.False(await database.Store.StartBrowserAsync(expired.Id, "expired-state", "nonce", expired.ExpiresAt, CancellationToken.None));
        Assert.NotNull(await database.Store.GetChallengeAsync(expired.Id, CancellationToken.None));
    }

    [MongoFact]
    public async Task SessionExpiryAndRevocationDoNotDependOnTtlCleanup()
    {
        await using var database = await TestDatabase.CreateAsync();
        var now = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()).UtcDateTime;
        var session = new Session(IdentityService.Hash("random-session-token"), "account", "Player", now.AddHours(1));
        await database.Store.CreateSessionAsync(session, CancellationToken.None);
        Assert.Equal(session, await database.Store.GetSessionAsync(session.TokenHash, now, CancellationToken.None));
        Assert.Null(await database.Store.GetSessionAsync(session.TokenHash, session.ExpiresAt, CancellationToken.None));
        var persisted = await database.Collection("sessions").Find(FilterDefinition<BsonDocument>.Empty).SingleAsync();
        Assert.Equal(session.TokenHash, persisted["_id"].AsString);
        Assert.False(persisted.Contains("SessionToken"));
        Assert.Equal(1, persisted["SchemaVersion"].AsInt32);
        await database.Store.RevokeSessionAsync(session.TokenHash, CancellationToken.None);
        Assert.Null(await database.Store.GetSessionAsync(session.TokenHash, now, CancellationToken.None));
        Assert.Equal(0, await database.Collection("sessions").CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));
    }

    [MongoFact]
    public async Task LegacyAccountsCanBeAdministeredAtomicallyWithPersistedAuditAndSessionRevocation()
    {
        await using var database = await TestDatabase.CreateAsync();
        var now = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()).UtcDateTime;
        var accountId = Guid.NewGuid().ToString("D");
        await database.Collection("accounts").InsertOneAsync(new BsonDocument
        {
            ["_id"] = accountId, ["SchemaVersion"] = 1, ["DiscordId"] = "987654321", ["DisplayName"] = "Legacy [player]",
            ["CreatedAt"] = now.AddDays(-1), ["LastLoginAt"] = now
        });
        var legacy = (await database.Store.GetAccountAsync(accountId, default))!;
        Assert.False(legacy.HasBetaAccess);
        Assert.False(legacy.IsBanned);
        Assert.Equal(0, legacy.Revision);
        Assert.Equal(0, legacy.SessionVersion);
        Assert.Equal(now, legacy.UpdatedAt);
        var found = await database.Store.ListAccountsAsync("[player]", 1, 25, default);
        Assert.Equal(1, found.Total);
        Assert.Equal(accountId, Assert.Single(found.Items).Id);
        Assert.Empty((await database.Store.ListAccountsAsync(".*", 1, 25, default)).Items);

        // Schema-one sessions had no epoch field and must also be physically removed.
        await database.Collection("sessions").InsertOneAsync(new BsonDocument
        {
            ["_id"] = IdentityService.Hash("legacy-session"), ["SchemaVersion"] = 1, ["AccountId"] = accountId,
            ["DisplayName"] = legacy.DisplayName, ["ExpiresAt"] = now.AddHours(1)
        });
        var attempts = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => database.Store.AdministerAccountAsync(
            accountId, 0, "ban", null, "Abuse report confirmed", "admin:discord:123", now, default)));
        var banned = Assert.Single(attempts, account => account is not null)!;
        Assert.True(banned.IsBanned);
        Assert.Equal(1, banned.Revision);
        Assert.Equal(1, banned.SessionVersion);
        Assert.Equal("Abuse report confirmed", banned.BanReason);
        Assert.Equal(0, await database.Collection("sessions").CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));
        Assert.Null(await database.Store.AdministerAccountAsync(accountId, 0, "unban", null, "Stale revision", "admin:discord:123", now, default));

        var granted = (await database.Store.AdministerAccountAsync(accountId, 1, "beta-access", true,
            "Beta approved", "admin:discord:456", now.AddMinutes(1), default))!;
        Assert.True(granted.HasBetaAccess);
        Assert.True(granted.IsBanned);
        var unbanned = (await database.Store.AdministerAccountAsync(accountId, 2, "unban", null,
            "Appeal accepted", "admin:discord:456", now.AddMinutes(2), default))!;
        Assert.False(unbanned.IsBanned);
        Assert.Null(unbanned.BanReason);
        Assert.Equal(1, unbanned.SessionVersion);
        var persisted = await database.Collection("accounts").Find(new BsonDocument("_id", accountId)).SingleAsync();
        Assert.Equal(2, persisted["SchemaVersion"].AsInt32);
        Assert.Equal(3, persisted["Revision"].ToInt64());
        var history = persisted["AdministrationHistory"].AsBsonArray;
        Assert.Equal(3, history.Count);
        Assert.Equal("admin:discord:123", history[0]["Actor"].AsString);
        Assert.Equal("ban", history[0]["Action"].AsString);
        Assert.Equal("Appeal accepted", history[2]["Reason"].AsString);
        // A subsequent Discord login must not reset moderation or entitlement state.
        var refreshed = await database.Store.UpsertAccountAsync("987654321", "New Discord Name", now.AddMinutes(3), default);
        Assert.Equal(3, refreshed.Revision);
        Assert.True(refreshed.HasBetaAccess);
        Assert.Equal(1, refreshed.SessionVersion);
    }

    private sealed class TestDatabase(MongoClient client, string name, MongoIdentityStore store) : IAsyncDisposable
    {
        public MongoIdentityStore Store => store;
        public IMongoCollection<BsonDocument> Collection(string collection) => client.GetDatabase(name).GetCollection<BsonDocument>(collection);

        public static async Task<TestDatabase> CreateAsync()
        {
            var settings = MongoClientSettings.FromConnectionString(Environment.GetEnvironmentVariable("ARKYA_TEST_MONGO")!);
            settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
            var client = new MongoClient(settings);
            var name = $"arkya_identity_test_{Guid.NewGuid():N}";
            var store = new MongoIdentityStore(client, Options.Create(new MongoOptions { ConnectionString = settings.ToString(), DatabaseName = name }));
            var test = new TestDatabase(client, name, store);
            try { await store.InitializeAsync(CancellationToken.None); return test; }
            catch { await test.DisposeAsync(); throw; }
        }

        public async ValueTask DisposeAsync() => await client.DropDatabaseAsync(name);
    }
}

internal sealed class MongoFactAttribute : FactAttribute
{
    public MongoFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ARKYA_TEST_MONGO")))
            Skip = "Set ARKYA_TEST_MONGO to run tests against a disposable MongoDB database.";
    }
}
