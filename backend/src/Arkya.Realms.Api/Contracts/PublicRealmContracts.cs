namespace Arkya.Realms.Api.Contracts;

public sealed record PublicRealmSummary(string RealmId, string DisplayName, string Status,
    int ReadyServerCount, int ServerCount, DateTimeOffset UpdatedAt, DateTimeOffset? LastObservedAt,
    int? PlayerCount);
