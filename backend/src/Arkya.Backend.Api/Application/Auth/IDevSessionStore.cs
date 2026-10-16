namespace Arkya.Backend.Api.Application.Auth;

public interface IDevSessionStore
{
    ValueTask<DevSession> GetOrCreateAsync(
        string displayName,
        DateTimeOffset now,
        TimeSpan sessionLifetime,
        CancellationToken cancellationToken);
}
