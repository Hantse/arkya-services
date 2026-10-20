namespace Arkya.Identity.Api.Domain;

public sealed record Account(string Id, string DiscordId, string DisplayName,
    bool IsBanned = false, string? BanReason = null, bool HasBetaAccess = false, long Revision = 0,
    DateTime CreatedAt = default, DateTime UpdatedAt = default, long SessionVersion = 0);
public sealed record Session(string TokenHash, string AccountId, string DisplayName, DateTime ExpiresAt,
    long SessionVersion = 0, bool HasBetaAccess = false);
public sealed record AccountAdministrationChange(long Revision, DateTime ChangedAt, string Actor, string Action, string Reason);
public enum ChallengeStatus { Pending, Processing, Completed, Failed, Consumed }
public sealed record LoginChallenge(
    string Id, string CodeChallenge, DateTime ExpiresAt,
    ChallengeStatus Status = ChallengeStatus.Pending,
    string? StateHash = null, string? BrowserNonceHash = null,
    string? AccountId = null, string? DisplayName = null, string? ReturnUrl = null, long AccountSessionVersion = 0);
