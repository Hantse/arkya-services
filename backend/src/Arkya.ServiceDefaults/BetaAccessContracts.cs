using System.Text.Json.Serialization;

namespace Arkya.ServiceDefaults.Accounts;

public sealed record BetaRegistrationResponse(bool Registered, DateTimeOffset? RegisteredAt, bool HasBetaAccess);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BetaRegistrationRequest;

public sealed record BetaRegistrationSummary(string AccountId, string DisplayName, string DiscordUserId,
    DateTimeOffset RegisteredAt, bool HasBetaAccess, bool IsBanned, long Revision);
public sealed record BetaRegistrationPage(IReadOnlyList<BetaRegistrationSummary> Items, long Total, int Page, int PageSize);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BetaGrantRequest([property: JsonRequired] long ExpectedRevision, string Reason);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BetaRandomGrantRequest([property: JsonRequired] Guid RequestId,
    [property: JsonRequired] int Count, string Reason);

public sealed record BetaRandomGrantResponse(Guid RequestId, int RequestedCount, int GrantedCount,
    IReadOnlyList<string> GrantedAccountIds, DateTimeOffset CreatedAt);
