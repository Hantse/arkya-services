namespace Arkya.ServiceDefaults.Reports;

public static class BugReportKinds
{
    public const string Bug = "bug";
    public const string Suggestion = "suggestion";
    public static readonly string[] All = [Bug, Suggestion];
}

public static class BugReportStatuses
{
    public const string New = "new";
    public const string Triaged = "triaged";
    public const string InProgress = "in_progress";
    public const string Resolved = "resolved";
    public const string Closed = "closed";
    public static readonly string[] All = [New, Triaged, InProgress, Resolved, Closed];
}

public static class BugReportSorts
{
    public const string Newest = "newest";
    public const string Oldest = "oldest";
    public const string Updated = "updated";
    public static readonly string[] All = [Newest, Oldest, Updated];
}

public sealed record BugReportListFilters(string? Search = null, string? Scene = null, string? ClientVersion = null,
    string? Platform = null, DateOnly? CreatedFrom = null, DateOnly? CreatedTo = null, string Sort = BugReportSorts.Newest);

public sealed record SubmitBugReportRequest(Guid ClientReportId, string? Title, string? Description,
    string? ClientVersion, string? Scene, string? Platform, string? ScreenshotBase64, string? Kind = BugReportKinds.Bug);

// PATCH replaces the complete triage state; the revision prevents overwriting another operator.
public sealed record UpdateBugReportRequest(long ExpectedRevision, string? Status, string[]? Tags, string? Resolution);

public sealed record BugReportScreenshot(int Width, int Height, int SizeBytes, string ContentType, string Sha256);

public sealed record BugReportChange(long Revision, DateTimeOffset ChangedAt, string Actor, string Status,
    string[] Tags, string Resolution);

public sealed record BugReportResponse(int SchemaVersion, Guid Id, Guid ClientReportId, Guid AccountId,
    string Title, string Description, string ClientVersion, string Scene, string Platform, string Status,
    string[] Tags, string Resolution, long Revision, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    BugReportScreenshot Screenshot, BugReportChange[] History, string Kind = BugReportKinds.Bug);

public sealed record BugReportListResponse(BugReportResponse[] Items, int Offset, int Limit, long Total);
