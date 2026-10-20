using Arkya.Identity.Api.Application;
using Arkya.Identity.Api.Domain;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Options;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace Arkya.Identity.Api.Infrastructure;

public sealed class MongoIdentityStore : IIdentityStore
{
    private readonly IMongoCollection<ChallengeDocument> challenges;
    private readonly IMongoCollection<AccountDocument> accounts;
    private readonly IMongoCollection<SessionDocument> sessions;

    public MongoIdentityStore(IMongoClient client, IOptions<MongoOptions> options)
    {
        var db = client.GetDatabase(options.Value.DatabaseName);
        challenges = db.GetCollection<ChallengeDocument>("login_challenges");
        accounts = db.GetCollection<AccountDocument>("accounts");
        sessions = db.GetCollection<SessionDocument>("sessions");
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await accounts.Indexes.CreateOneAsync(new CreateIndexModel<AccountDocument>(Builders<AccountDocument>.IndexKeys.Ascending(x => x.DiscordId),
            new CreateIndexOptions { Unique = true, Name = "discord_id_unique" }), cancellationToken: cancellationToken);
        await challenges.Indexes.CreateManyAsync([
            new(Builders<ChallengeDocument>.IndexKeys.Ascending(x => x.ExpiresAt), new CreateIndexOptions { ExpireAfter = TimeSpan.Zero, Name = "challenge_expiry" }),
            new(Builders<ChallengeDocument>.IndexKeys.Ascending(x => x.StateHash), new CreateIndexOptions<ChallengeDocument>
            {
                Unique = true, Name = "oauth_state_unique", PartialFilterExpression = Builders<ChallengeDocument>.Filter.Type(x => x.StateHash, MongoDB.Bson.BsonType.String)
            })
        ], cancellationToken);
        await sessions.Indexes.CreateManyAsync([
            new(Builders<SessionDocument>.IndexKeys.Ascending(x => x.ExpiresAt), new CreateIndexOptions { ExpireAfter = TimeSpan.Zero, Name = "session_expiry" }),
            new(Builders<SessionDocument>.IndexKeys.Ascending(x => x.AccountId), new CreateIndexOptions { Name = "account_sessions" })
        ], cancellationToken);
    }

    public Task CreateChallengeAsync(LoginChallenge challenge, CancellationToken cancellationToken) =>
        challenges.InsertOneAsync(new ChallengeDocument
        {
            Id = challenge.Id, CodeChallenge = challenge.CodeChallenge, ExpiresAt = challenge.ExpiresAt, ReturnUrl = challenge.ReturnUrl
        }, cancellationToken: cancellationToken);

    public async Task<LoginChallenge?> GetChallengeAsync(string id, CancellationToken cancellationToken) =>
        (await challenges.Find(x => x.Id == id).FirstOrDefaultAsync(cancellationToken))?.ToDomain();

    public async Task<bool> StartBrowserAsync(string id, string stateHash, string browserNonceHash, DateTime now, CancellationToken cancellationToken)
    {
        var result = await challenges.UpdateOneAsync(x => x.Id == id && x.Status == ChallengeStatus.Pending && x.StateHash == null && x.ExpiresAt > now,
            Builders<ChallengeDocument>.Update.Set(x => x.StateHash, stateHash).Set(x => x.BrowserNonceHash, browserNonceHash), cancellationToken: cancellationToken);
        return result.ModifiedCount == 1;
    }

    public async Task<LoginChallenge?> FindStateAsync(string stateHash, CancellationToken cancellationToken) =>
        (await challenges.Find(x => x.StateHash == stateHash).FirstOrDefaultAsync(cancellationToken))?.ToDomain();

    public async Task<LoginChallenge?> ClaimStateAsync(string stateHash, string browserNonceHash, DateTime now, CancellationToken cancellationToken)
    {
        var result = await challenges.FindOneAndUpdateAsync(x => x.StateHash == stateHash && x.BrowserNonceHash == browserNonceHash &&
            x.Status == ChallengeStatus.Pending && x.ExpiresAt > now,
            Builders<ChallengeDocument>.Update.Set(x => x.Status, ChallengeStatus.Processing), cancellationToken: cancellationToken);
        return result?.ToDomain();
    }

    public Task CompleteChallengeAsync(string id, Account? account, CancellationToken cancellationToken) =>
        challenges.UpdateOneAsync(x => x.Id == id && x.Status == ChallengeStatus.Processing,
            Builders<ChallengeDocument>.Update.Set(x => x.Status, account is null ? ChallengeStatus.Failed : ChallengeStatus.Completed)
                .Set(x => x.AccountId, account?.Id).Set(x => x.DisplayName, account?.DisplayName), cancellationToken: cancellationToken);

    public async Task<LoginChallenge?> RedeemChallengeAsync(string id, string codeChallenge, DateTime now, CancellationToken cancellationToken) =>
        (await challenges.FindOneAndUpdateAsync(x => x.Id == id && x.CodeChallenge == codeChallenge && x.Status == ChallengeStatus.Completed && x.ExpiresAt > now,
            Builders<ChallengeDocument>.Update.Set(x => x.Status, ChallengeStatus.Consumed), cancellationToken: cancellationToken))?.ToDomain();

    public async Task<Account> UpsertAccountAsync(string discordId, string displayName, DateTime now, CancellationToken cancellationToken)
    {
        var update = Builders<AccountDocument>.Update.SetOnInsert(x => x.Id, Guid.NewGuid().ToString("D"))
            .SetOnInsert(x => x.SchemaVersion, 1).SetOnInsert(x => x.CreatedAt, now)
            .Set(x => x.DiscordId, discordId).Set(x => x.DisplayName, displayName).Set(x => x.LastLoginAt, now);
        AccountDocument account;
        try
        {
            account = await accounts.FindOneAndUpdateAsync(x => x.DiscordId == discordId, update,
                new() { IsUpsert = true, ReturnDocument = ReturnDocument.After }, cancellationToken);
        }
        catch (MongoCommandException exception) when (exception.Code == 11000)
        {
            // Two first-time logins may race on the unique provider id.
            account = await accounts.FindOneAndUpdateAsync(x => x.DiscordId == discordId,
                Builders<AccountDocument>.Update.Set(x => x.DisplayName, displayName).Set(x => x.LastLoginAt, now),
                new() { ReturnDocument = ReturnDocument.After }, cancellationToken);
        }
        return new(account.Id, account.DiscordId, account.DisplayName);
    }

    public async Task<Account?> GetAccountAsync(string accountId, CancellationToken cancellationToken)
    {
        var account = await accounts.Find(value => value.Id == accountId).FirstOrDefaultAsync(cancellationToken);
        return account is null ? null : new(account.Id, account.DiscordId, account.DisplayName);
    }

    public Task CreateSessionAsync(Session session, CancellationToken cancellationToken) => sessions.InsertOneAsync(new SessionDocument
    {
        TokenHash = session.TokenHash, AccountId = session.AccountId, DisplayName = session.DisplayName, ExpiresAt = session.ExpiresAt
    }, cancellationToken: cancellationToken);

    public async Task<Session?> GetSessionAsync(string tokenHash, DateTime now, CancellationToken cancellationToken)
    {
        var result = await sessions.Find(x => x.TokenHash == tokenHash && x.ExpiresAt > now).FirstOrDefaultAsync(cancellationToken);
        return result is null ? null : new(result.TokenHash, result.AccountId, result.DisplayName, result.ExpiresAt);
    }

    public Task RevokeSessionAsync(string tokenHash, CancellationToken cancellationToken) =>
        sessions.DeleteOneAsync(x => x.TokenHash == tokenHash, cancellationToken);

    private sealed class ChallengeDocument
    {
        [BsonId] public string Id { get; set; } = "";
        public int SchemaVersion { get; set; } = 1;
        public string CodeChallenge { get; set; } = "";
        public DateTime ExpiresAt { get; set; }
        public ChallengeStatus Status { get; set; }
        public string? StateHash { get; set; }
        public string? BrowserNonceHash { get; set; }
        public string? AccountId { get; set; }
        public string? DisplayName { get; set; }
        [BsonIgnoreIfNull] public string? ReturnUrl { get; set; }
        public LoginChallenge ToDomain() => new(Id, CodeChallenge, ExpiresAt, Status, StateHash, BrowserNonceHash, AccountId, DisplayName, ReturnUrl);
    }

    private sealed class AccountDocument
    {
        [BsonId] public string Id { get; set; } = "";
        public int SchemaVersion { get; set; } = 1;
        public string DiscordId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public DateTime LastLoginAt { get; set; }
    }

    private sealed class SessionDocument
    {
        [BsonId] public string TokenHash { get; set; } = "";
        public int SchemaVersion { get; set; } = 1;
        public string AccountId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public DateTime ExpiresAt { get; set; }
    }
}

public sealed class IdentityIndexInitializer(MongoIdentityStore store) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => store.InitializeAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
