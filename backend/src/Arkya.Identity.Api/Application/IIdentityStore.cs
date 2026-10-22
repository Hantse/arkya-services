using Arkya.Identity.Api.Domain;

namespace Arkya.Identity.Api.Application;

public interface IIdentityStore
{
    Task CreateChallengeAsync(LoginChallenge challenge, CancellationToken cancellationToken);
    Task<LoginChallenge?> GetChallengeAsync(string id, CancellationToken cancellationToken);
    Task<bool> StartBrowserAsync(string id, string stateHash, string browserNonceHash, DateTime now, CancellationToken cancellationToken);
    Task<LoginChallenge?> FindStateAsync(string stateHash, CancellationToken cancellationToken);
    Task<LoginChallenge?> ClaimStateAsync(string stateHash, string browserNonceHash, DateTime now, CancellationToken cancellationToken);
    Task CompleteChallengeAsync(string id, Account? account, CancellationToken cancellationToken);
    Task<LoginChallenge?> RedeemChallengeAsync(string id, string codeChallenge, DateTime now, CancellationToken cancellationToken);
    Task<Account> UpsertAccountAsync(string discordId, string displayName, DateTime now, CancellationToken cancellationToken);
    Task<Account?> GetAccountAsync(string accountId, CancellationToken cancellationToken);
    Task<(IReadOnlyList<Account> Items, long Total)> ListAccountsAsync(string? search, int page, int pageSize, CancellationToken cancellationToken);
    Task<Account?> AdministerAccountAsync(string accountId, long expectedRevision, string action, bool? hasBetaAccess,
        string reason, string actor, DateTime now, CancellationToken cancellationToken);
    Task CreateSessionAsync(Session session, CancellationToken cancellationToken);
    Task<Session?> GetSessionAsync(string tokenHash, DateTime now, CancellationToken cancellationToken);
    Task RevokeSessionAsync(string tokenHash, CancellationToken cancellationToken);
    Task CreateRefreshSessionAsync(RefreshSession session, CancellationToken cancellationToken);
    Task<RefreshSession?> GetRefreshSessionAsync(string id, CancellationToken cancellationToken);
    Task<RefreshSession?> RotateRefreshSessionAsync(string id, string tokenHash, string replacementHash, DateTime now, CancellationToken cancellationToken);
    Task RevokeRefreshSessionAsync(string id, CancellationToken cancellationToken);
}

public interface IDiscordIdentityClient
{
    Task<DiscordUser> ExchangeCodeAsync(string code, CancellationToken cancellationToken);
}

public sealed record DiscordUser(string Id, string DisplayName);
