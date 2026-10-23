using System.Text.Json.Serialization;

namespace Arkya.Identity.Api.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ChallengeRequest(string? CodeChallenge, string? ReturnUrl = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ExchangeRequest(string? ChallengeId, string? CodeVerifier, bool UseRefreshCookie = false, bool UseRefreshToken = false);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record NativeRefreshRequest(string? RefreshToken)
{
    public override string ToString() => nameof(NativeRefreshRequest);
}
public sealed record NativeSessionResponse(string AccountId, string DisplayName, string SessionToken, DateTime ExpiresAt,
    string RefreshToken, DateTime RefreshExpiresAt)
{
    public override string ToString() => nameof(NativeSessionResponse);
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record IntrospectionRequest(string? SessionToken);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SessionReferenceRequest(string? SessionReference);
public sealed record AccountSessionResponse(string AccountId, string DisplayName, DateTime ExpiresAt,
    bool HasBetaAccess = false, bool IsBanned = false);
