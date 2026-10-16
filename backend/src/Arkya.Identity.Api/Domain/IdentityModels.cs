namespace Arkya.Identity.Api.Domain;

public sealed record Account(string Id, string DiscordId, string DisplayName);
public sealed record Session(string TokenHash, string AccountId, string DisplayName, DateTime ExpiresAt);
public enum ChallengeStatus { Pending, Processing, Completed, Failed, Consumed }
public sealed record LoginChallenge(
    string Id, string CodeChallenge, DateTime ExpiresAt,
    ChallengeStatus Status = ChallengeStatus.Pending,
    string? StateHash = null, string? BrowserNonceHash = null,
    string? AccountId = null, string? DisplayName = null);
