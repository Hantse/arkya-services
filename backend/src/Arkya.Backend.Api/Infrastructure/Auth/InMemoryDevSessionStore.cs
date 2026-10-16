using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

using Arkya.Backend.Api.Application.Auth;

namespace Arkya.Backend.Api.Infrastructure.Auth;

public sealed class InMemoryDevSessionStore : IDevSessionStore
{
    private readonly ConcurrentDictionary<string, DevSession> sessionsByDisplayName = new(
        StringComparer.OrdinalIgnoreCase);

    public ValueTask<DevSession> GetOrCreateAsync(
        string displayName,
        DateTimeOffset now,
        TimeSpan sessionLifetime,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var key = NormalizeDisplayNameKey(displayName);
        var session = sessionsByDisplayName.AddOrUpdate(
            key,
            _ => CreateSession(key, displayName, now, sessionLifetime),
            (_, existingSession) => existingSession.ExpiresAt <= now
                ? CreateSession(key, displayName, now, sessionLifetime)
                : existingSession with { DisplayName = displayName });

        return ValueTask.FromResult(session);
    }

    private static DevSession CreateSession(
        string key,
        string displayName,
        DateTimeOffset now,
        TimeSpan sessionLifetime)
    {
        var stableHash = CreateStableHash(key);

        return new DevSession(
            $"dev-player-{stableHash[..12]}",
            displayName,
            $"dev-session-{stableHash[..24]}",
            now,
            now.Add(sessionLifetime));
    }

    private static string NormalizeDisplayNameKey(string displayName)
    {
        return displayName.Trim().ToUpperInvariant();
    }

    private static string CreateStableHash(string value)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));

        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
