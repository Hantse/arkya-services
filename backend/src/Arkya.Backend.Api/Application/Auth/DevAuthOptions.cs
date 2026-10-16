namespace Arkya.Backend.Api.Application.Auth;

public sealed class DevAuthOptions
{
    public const string SectionName = "DevAuth";

    public string DefaultDisplayName { get; init; } = "Local Player";

    public TimeSpan SessionLifetime { get; init; } = TimeSpan.FromHours(12);
}
