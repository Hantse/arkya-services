namespace Arkya.Backend.Api.Application.Auth;

public sealed record DevSession(
    string PlayerId,
    string DisplayName,
    string SessionToken,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);
