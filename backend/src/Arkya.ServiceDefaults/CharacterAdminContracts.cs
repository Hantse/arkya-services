namespace Arkya.ServiceDefaults.Characters;

public sealed record AdminCharacterSummary(string CharacterId, string AccountId, string DisplayName,
    string RealmId, string Race, string Gender, DateTimeOffset CreatedAt, bool IsOnline,
    DateTimeOffset? LeaseExpiresAt);

public sealed record AdminCharacterPage(AdminCharacterSummary[] Items, long Total, int Page, int PageSize);

public sealed record AdminCharacterOverview(long TotalCharacters, long OnlineCharacters, RealmOnlineCount[] Realms);

public sealed record RealmOnlineCount(string RealmId, long OnlineCharacters);
