using Arkya.Backend.Api.Contracts.Auth;

using Microsoft.Extensions.Options;

namespace Arkya.Backend.Api.Application.Auth;

public sealed class DevAuthService
{
    private static readonly TimeSpan FallbackSessionLifetime = TimeSpan.FromHours(12);

    private readonly IDevSessionStore sessionStore;
    private readonly TimeProvider timeProvider;
    private readonly IOptions<DevAuthOptions> options;

    public DevAuthService(
        IDevSessionStore sessionStore,
        TimeProvider timeProvider,
        IOptions<DevAuthOptions> options)
    {
        this.sessionStore = sessionStore;
        this.timeProvider = timeProvider;
        this.options = options;
    }

    public ValueTask<DevSession> LoginAsync(
        DevLoginRequest? request,
        CancellationToken cancellationToken)
    {
        var displayName = ResolveDisplayName(
            request?.DisplayName,
            options.Value.DefaultDisplayName);

        var lifetime = options.Value.SessionLifetime > TimeSpan.Zero
            ? options.Value.SessionLifetime
            : FallbackSessionLifetime;

        return sessionStore.GetOrCreateAsync(
            displayName,
            timeProvider.GetUtcNow(),
            lifetime,
            cancellationToken);
    }

    private static string ResolveDisplayName(string? requestedDisplayName, string fallbackDisplayName)
    {
        var displayName = string.IsNullOrWhiteSpace(requestedDisplayName)
            ? fallbackDisplayName
            : requestedDisplayName.Trim();

        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = "Local Player";
        }

        return displayName.Length <= 32
            ? displayName
            : displayName[..32];
    }
}
