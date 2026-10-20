using System.Text.Json.Serialization;

namespace Arkya.Identity.Api.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ChallengeRequest(string? CodeChallenge, string? ReturnUrl = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ExchangeRequest(string? ChallengeId, string? CodeVerifier);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record IntrospectionRequest(string? SessionToken);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SessionReferenceRequest(string? SessionReference);
public sealed record AccountSessionResponse(string AccountId, string DisplayName, DateTime ExpiresAt);
