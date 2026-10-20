using System.Text.Json.Serialization;

namespace Arkya.ServiceDefaults.Accounts;

public sealed record AdminAccountSummary(string AccountId, string DisplayName, string DiscordUserId,
    bool IsBanned, string? BanReason, bool HasBetaAccess, long Revision, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record AdminAccountPage(IReadOnlyList<AdminAccountSummary> Items, long Total, int Page, int PageSize);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AccountBanRequest([property: JsonRequired] long ExpectedRevision, string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AccountUnbanRequest([property: JsonRequired] long ExpectedRevision, string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AccountBetaAccessRequest([property: JsonRequired] long ExpectedRevision,
    [property: JsonRequired] bool HasBetaAccess, string Reason);
