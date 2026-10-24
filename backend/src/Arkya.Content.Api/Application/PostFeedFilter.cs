namespace Arkya.Content.Api.Application;

public sealed record PostFeedFilter(string? Query = null, string? GameVersion = null,
    DateTimeOffset? From = null, DateTimeOffset? ToExclusive = null, bool OldestFirst = false);
