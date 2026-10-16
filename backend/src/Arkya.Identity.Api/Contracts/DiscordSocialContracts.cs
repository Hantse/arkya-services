using System.Text.Json.Serialization;

namespace Arkya.Identity.Api.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DiscordSocialExchangeRequest(string? Code, string? CodeVerifier, string? RedirectUri);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DiscordSocialRefreshRequest(string? RefreshTicket);
public sealed record DiscordSocialConfiguration(string ApplicationId, string Scopes, string RedirectUri);
public sealed record DiscordSocialTokenResponse(string AccessToken, string RefreshTicket, string TokenType, int ExpiresIn, string Scope, string DiscordUserId);
