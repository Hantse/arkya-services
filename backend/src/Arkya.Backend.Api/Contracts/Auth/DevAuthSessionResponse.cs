using Arkya.Backend.Api.Application.Auth;

namespace Arkya.Backend.Api.Contracts.Auth;

public sealed record DevAuthSessionResponse(
    string PlayerId,
    string DisplayName,
    string SessionToken,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt)
{
    public static DevAuthSessionResponse FromSession(DevSession session)
    {
        return new DevAuthSessionResponse(
            session.PlayerId,
            session.DisplayName,
            session.SessionToken,
            session.CreatedAt,
            session.ExpiresAt);
    }
}
