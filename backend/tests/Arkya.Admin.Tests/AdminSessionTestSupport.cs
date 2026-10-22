using Arkya.Admin.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Arkya.Admin.Tests;

internal static class AdminSessionTestServices
{
    public static void AddTestSessions(this IServiceCollection services, MemoryAdminSessionStore? store = null, TimeProvider? time = null)
    {
        services.RemoveAll<IAdminSessionStore>();
        services.AddSingleton<IAdminSessionStore>(store ?? new MemoryAdminSessionStore());
        services.RemoveAll<TimeProvider>();
        services.AddSingleton(time ?? TimeProvider.System);
        var initializer = services.Single(service => service.ServiceType == typeof(IHostedService) &&
            service.ImplementationType == typeof(AdminSessionIndexInitializer));
        services.Remove(initializer);
    }
}

internal sealed class AdminTestTime : TimeProvider
{
    private DateTimeOffset now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan value) => now += value;
}

internal sealed class MemoryAdminSessionStore : IAdminSessionStore
{
    private readonly Dictionary<string, AdminSession> sessions = [];
    private readonly object sync = new();
    public AdminSession[] All { get { lock (sync) return sessions.Values.ToArray(); } }
    public bool Unavailable { get; set; }

    public Task CreateAsync(AdminSession session, CancellationToken cancellationToken)
    {
        if (Unavailable) throw new TimeoutException();
        lock (sync) sessions.Add(session.Id, session);
        return Task.CompletedTask;
    }

    public Task<AdminSession?> GetAsync(string id, CancellationToken cancellationToken)
    {
        if (Unavailable) throw new TimeoutException();
        lock (sync) return Task.FromResult(sessions.GetValueOrDefault(id));
    }

    public Task<bool> RotateAsync(string id, string expectedHash, string nextHash, DateTime now, CancellationToken cancellationToken)
    {
        if (Unavailable) throw new TimeoutException();
        lock (sync)
        {
            var current = sessions.GetValueOrDefault(id);
            if (current is null || current.RefreshTokenHash != expectedHash || current.ExpiresAt <= now || current.SchemaVersion != 1 ||
                current.UsedTokenHashes.Length >= AdminSession.MaximumRotations) return Task.FromResult(false);
            sessions[id] = current with { RefreshTokenHash = nextHash, RotatedAt = now, UsedTokenHashes = [.. current.UsedTokenHashes, expectedHash] };
            return Task.FromResult(true);
        }
    }

    public Task RevokeAsync(string id, CancellationToken cancellationToken)
    {
        if (Unavailable) throw new TimeoutException();
        lock (sync) sessions.Remove(id);
        return Task.CompletedTask;
    }
}
