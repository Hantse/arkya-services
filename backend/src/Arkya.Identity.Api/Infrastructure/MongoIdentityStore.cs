using Arkya.Identity.Api.Application;
using Arkya.Identity.Api.Domain;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Options;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using MongoDB.Bson;
using System.Text.RegularExpressions;

namespace Arkya.Identity.Api.Infrastructure;

public sealed partial class MongoIdentityStore : IIdentityStore, IBetaAccessStore
{
    private readonly IMongoCollection<ChallengeDocument> challenges;
    private readonly IMongoCollection<AccountDocument> accounts;
    private readonly IMongoCollection<SessionDocument> sessions;
    private readonly IMongoCollection<RefreshSessionDocument> refreshSessions;

    public MongoIdentityStore(IMongoClient client, IOptions<MongoOptions> options)
    {
        var db = client.GetDatabase(options.Value.DatabaseName);
        challenges = db.GetCollection<ChallengeDocument>("login_challenges");
        accounts = db.GetCollection<AccountDocument>("accounts");
        sessions = db.GetCollection<SessionDocument>("sessions");
        refreshSessions = db.GetCollection<RefreshSessionDocument>("refresh_sessions");
        betaGrantOperations = db.GetCollection<BetaGrantOperationDocument>("beta_grant_operations");
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await InitializeBetaAsync(cancellationToken);
        await accounts.Indexes.CreateOneAsync(new CreateIndexModel<AccountDocument>(Builders<AccountDocument>.IndexKeys.Ascending(x => x.DiscordId),
            new CreateIndexOptions { Unique = true, Name = "discord_id_unique" }), cancellationToken: cancellationToken);
        await accounts.Indexes.CreateOneAsync(new CreateIndexModel<AccountDocument>(Builders<AccountDocument>.IndexKeys.Ascending(x => x.DisplayName).Ascending(x => x.Id),
            new CreateIndexOptions { Name = "account_admin_name" }), cancellationToken: cancellationToken);
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
        await refreshSessions.Indexes.CreateManyAsync([
            new(Builders<RefreshSessionDocument>.IndexKeys.Ascending(x => x.ExpiresAt), new CreateIndexOptions { ExpireAfter = TimeSpan.Zero, Name = "refresh_expiry" }),
            new(Builders<RefreshSessionDocument>.IndexKeys.Ascending(x => x.AccountId), new CreateIndexOptions { Name = "account_refresh_sessions" })
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
                .Set(x => x.AccountId, account?.Id).Set(x => x.DisplayName, account?.DisplayName)
                .Set(x => x.AccountSessionVersion, account?.SessionVersion ?? 0), cancellationToken: cancellationToken);

    public async Task<LoginChallenge?> RedeemChallengeAsync(string id, string codeChallenge, DateTime now, CancellationToken cancellationToken) =>
        (await challenges.FindOneAndUpdateAsync(x => x.Id == id && x.CodeChallenge == codeChallenge && x.Status == ChallengeStatus.Completed && x.ExpiresAt > now,
            Builders<ChallengeDocument>.Update.Set(x => x.Status, ChallengeStatus.Consumed), cancellationToken: cancellationToken))?.ToDomain();

    public async Task<Account> UpsertAccountAsync(string discordId, string displayName, DateTime now, CancellationToken cancellationToken)
    {
        var update = Builders<AccountDocument>.Update.SetOnInsert(x => x.Id, Guid.NewGuid().ToString("D"))
            .SetOnInsert(x => x.SchemaVersion, 2).SetOnInsert(x => x.CreatedAt, now)
            .SetOnInsert(x => x.Revision, 0).SetOnInsert(x => x.SessionVersion, 0)
            .SetOnInsert(x => x.IsBanned, false).SetOnInsert(x => x.HasBetaAccess, false)
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
        return account.ToDomain();
    }

    public async Task<Account?> GetAccountAsync(string accountId, CancellationToken cancellationToken)
    {
        var account = await accounts.Find(value => value.Id == accountId).FirstOrDefaultAsync(cancellationToken);
        return account?.ToDomain();
    }

    public async Task<(IReadOnlyList<Account> Items, long Total)> ListAccountsAsync(string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        var filters = Builders<AccountDocument>.Filter;
        var filter = filters.Empty;
        if (!string.IsNullOrWhiteSpace(search))
        {
            var literal = new BsonRegularExpression(Regex.Escape(search), "i");
            filter = filters.Or(filters.Eq(x => x.Id, search), filters.Eq(x => x.DiscordId, search), filters.Regex(x => x.DisplayName, literal));
        }
        var total = await accounts.CountDocumentsAsync(filter, cancellationToken: cancellationToken);
        var result = await accounts.Find(filter).SortBy(x => x.DisplayName).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Limit(pageSize).ToListAsync(cancellationToken);
        return (result.Select(account => account.ToDomain()).ToArray(), total);
    }

    public async Task<Account?> AdministerAccountAsync(string accountId, long expectedRevision, string action, bool? hasBetaAccess,
        string reason, string actor, DateTime now, CancellationToken cancellationToken)
    {
        var filters = Builders<AccountDocument>.Filter;
        var revision = expectedRevision == 0
            ? filters.Or(filters.Eq(x => x.Revision, 0), filters.Exists(x => x.Revision, false))
            : filters.Eq(x => x.Revision, expectedRevision);
        var updates = Builders<AccountDocument>.Update;
        var update = updates.Inc(x => x.Revision, 1).Set(x => x.SchemaVersion, 2).Set(x => x.UpdatedAt, now)
            .PushEach(x => x.AdministrationHistory, [new AccountAdministrationChange(expectedRevision + 1, now, actor, action, reason)], slice: -100);
        update = action switch
        {
            "ban" => update.Set(x => x.IsBanned, true).Set(x => x.BanReason, reason).Inc(x => x.SessionVersion, 1),
            "unban" => update.Set(x => x.IsBanned, false).Set(x => x.BanReason, null),
            "beta-access" when hasBetaAccess.HasValue => update.Set(x => x.HasBetaAccess, hasBetaAccess.Value),
            _ => throw new ArgumentException("Unknown account administration action.", nameof(action))
        };
        var account = await accounts.FindOneAndUpdateAsync(filters.Eq(x => x.Id, accountId) & revision &
            filters.Eq(x => x.PendingBetaGrantOperationId, null), update,
            new() { ReturnDocument = ReturnDocument.After }, cancellationToken);
        // SessionVersion is the durable revocation barrier, including concurrent session creation.
        if (account is not null && action == "ban")
            await sessions.DeleteManyAsync(Builders<SessionDocument>.Filter.Eq(x => x.AccountId, accountId) &
                (Builders<SessionDocument>.Filter.Lt(x => x.SessionVersion, account.SessionVersion) |
                 Builders<SessionDocument>.Filter.Exists(x => x.SessionVersion, false)), cancellationToken);
        return account?.ToDomain();
    }

    public Task CreateSessionAsync(Session session, CancellationToken cancellationToken) => sessions.InsertOneAsync(new SessionDocument
    {
        TokenHash = session.TokenHash, AccountId = session.AccountId, DisplayName = session.DisplayName, ExpiresAt = session.ExpiresAt,
        SessionVersion = session.SessionVersion, RefreshSessionId = session.RefreshSessionId
    }, cancellationToken: cancellationToken);

    public async Task<Session?> GetSessionAsync(string tokenHash, DateTime now, CancellationToken cancellationToken)
    {
        var result = await sessions.Find(x => x.TokenHash == tokenHash && x.ExpiresAt > now).FirstOrDefaultAsync(cancellationToken);
        return result is null ? null : new(result.TokenHash, result.AccountId, result.DisplayName, result.ExpiresAt, result.SessionVersion,
            RefreshSessionId: result.RefreshSessionId);
    }

    public Task RevokeSessionAsync(string tokenHash, CancellationToken cancellationToken) =>
        sessions.DeleteOneAsync(x => x.TokenHash == tokenHash, cancellationToken);

    public Task CreateRefreshSessionAsync(RefreshSession session, CancellationToken cancellationToken) =>
        refreshSessions.InsertOneAsync(new RefreshSessionDocument
        {
            Id = session.Id, AccountId = session.AccountId, SessionVersion = session.SessionVersion,
            TokenHash = session.TokenHash, ExpiresAt = session.ExpiresAt, UsedTokenHashes = session.UsedTokenHashes,
            Revoked = session.Revoked
        }, cancellationToken: cancellationToken);

    public async Task<RefreshSession?> GetRefreshSessionAsync(string id, CancellationToken cancellationToken) =>
        (await refreshSessions.Find(x => x.Id == id).FirstOrDefaultAsync(cancellationToken))?.ToDomain();

    public async Task<RefreshSession?> RotateRefreshSessionAsync(string id, string tokenHash, string replacementHash, DateTime now,
        CancellationToken cancellationToken) =>
        (await refreshSessions.FindOneAndUpdateAsync(x => x.Id == id && x.TokenHash == tokenHash && !x.Revoked &&
            x.ExpiresAt > now && x.UsedTokenHashes.Length < RefreshSession.MaximumRotations,
            Builders<RefreshSessionDocument>.Update.Set(x => x.TokenHash, replacementHash).Push(x => x.UsedTokenHashes, tokenHash),
            new() { ReturnDocument = ReturnDocument.After }, cancellationToken))?.ToDomain();

    public Task RevokeRefreshSessionAsync(string id, CancellationToken cancellationToken) =>
        refreshSessions.UpdateOneAsync(x => x.Id == id, Builders<RefreshSessionDocument>.Update.Set(x => x.Revoked, true),
            cancellationToken: cancellationToken);

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
        public long AccountSessionVersion { get; set; }
        [BsonIgnoreIfNull] public string? ReturnUrl { get; set; }
        public LoginChallenge ToDomain() => new(Id, CodeChallenge, ExpiresAt, Status, StateHash, BrowserNonceHash, AccountId, DisplayName, ReturnUrl, AccountSessionVersion);
    }

    private sealed class AccountDocument
    {
        [BsonId] public string Id { get; set; } = "";
        public int SchemaVersion { get; set; } = 1;
        public string DiscordId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public DateTime LastLoginAt { get; set; }
        public bool IsBanned { get; set; }
        public string? BanReason { get; set; }
        public bool HasBetaAccess { get; set; }
        public long Revision { get; set; }
        public long SessionVersion { get; set; }
        [BsonIgnoreIfNull] public DateTime? BetaRegisteredAt { get; set; }
        [BsonIgnoreIfNull] public string? PendingBetaGrantOperationId { get; set; }
        public DateTime UpdatedAt { get; set; }
        public AccountAdministrationChange[] AdministrationHistory { get; set; } = [];
        public Account ToDomain() => new(Id, DiscordId, DisplayName, IsBanned, BanReason, HasBetaAccess, Revision,
            CreatedAt, UpdatedAt == default ? (LastLoginAt == default ? CreatedAt : LastLoginAt) : UpdatedAt, SessionVersion, BetaRegisteredAt);
    }

    private sealed class SessionDocument
    {
        [BsonId] public string TokenHash { get; set; } = "";
        public int SchemaVersion { get; set; } = 1;
        public string AccountId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public DateTime ExpiresAt { get; set; }
        public long SessionVersion { get; set; }
        [BsonIgnoreIfNull] public string? RefreshSessionId { get; set; }
    }

    private sealed class RefreshSessionDocument
    {
        [BsonId] public string Id { get; set; } = "";
        public int SchemaVersion { get; set; } = 1;
        public string AccountId { get; set; } = "";
        public long SessionVersion { get; set; }
        public string TokenHash { get; set; } = "";
        public DateTime ExpiresAt { get; set; }
        public string[] UsedTokenHashes { get; set; } = [];
        public bool Revoked { get; set; }
        public RefreshSession ToDomain() => new(Id, AccountId, SessionVersion, TokenHash, ExpiresAt, UsedTokenHashes, Revoked);
    }
}

public sealed class IdentityIndexInitializer(MongoIdentityStore store) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => store.InitializeAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
