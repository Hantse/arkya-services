using Arkya.Reports.Api.Application;
using Arkya.ServiceDefaults.Reports;
using MongoDB.Bson.Serialization.Attributes;

namespace Arkya.Reports.Api.Infrastructure.Persistence;

internal sealed class ReportDocument
{
    [BsonId] public string Id { get; set; } = "";
    public int SchemaVersion { get; set; } = 1;
    public string ClientReportId { get; set; } = "";
    public string AccountId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string ClientVersion { get; set; } = "";
    public string Scene { get; set; } = "";
    public string Platform { get; set; } = "";
    public string Kind { get; set; } = BugReportKinds.Bug;
    public string Status { get; set; } = BugReportStatuses.New;
    public string[] Tags { get; set; } = [];
    public string Resolution { get; set; } = "";
    public long Revision { get; set; } = 1;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string PayloadHash { get; set; } = "";
    public int ScreenshotWidth { get; set; }
    public int ScreenshotHeight { get; set; }
    public int ScreenshotSizeBytes { get; set; }
    public string ScreenshotSha256 { get; set; } = "";
    public byte[] ScreenshotBytes { get; set; } = [];
    public ReportChangeDocument[] History { get; set; } = [];

    public BugReportResponse ToResponse() => new(SchemaVersion, Guid.Parse(Id), Guid.Parse(ClientReportId), Guid.Parse(AccountId),
        Title, Description, ClientVersion, Scene, Platform, Status, Tags, Resolution, Revision,
        Utc(CreatedAt), Utc(UpdatedAt), new(ScreenshotWidth, ScreenshotHeight, ScreenshotSizeBytes, "image/png", ScreenshotSha256),
        History.Select(item => item.ToResponse()).ToArray(), Kind);

    public static ReportDocument From(StoredReport stored)
    {
        var report = stored.Report;
        return new()
        {
            Id = report.Id.ToString("D"), SchemaVersion = report.SchemaVersion,
            ClientReportId = report.ClientReportId.ToString("D"), AccountId = report.AccountId.ToString("D"),
            Title = report.Title, Description = report.Description, ClientVersion = report.ClientVersion,
            Scene = report.Scene, Platform = report.Platform, Kind = report.Kind, Status = report.Status, Tags = report.Tags,
            Resolution = report.Resolution, Revision = report.Revision, CreatedAt = report.CreatedAt.UtcDateTime,
            UpdatedAt = report.UpdatedAt.UtcDateTime, PayloadHash = stored.PayloadHash, ScreenshotWidth = report.Screenshot.Width,
            ScreenshotHeight = report.Screenshot.Height, ScreenshotSizeBytes = report.Screenshot.SizeBytes,
            ScreenshotSha256 = report.Screenshot.Sha256, ScreenshotBytes = stored.Screenshot
        };
    }

    internal static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}

internal sealed class ReportChangeDocument
{
    public long Revision { get; set; }
    public DateTime ChangedAt { get; set; }
    public string Actor { get; set; } = "";
    public string Status { get; set; } = "";
    public string[] Tags { get; set; } = [];
    public string Resolution { get; set; } = "";
    public BugReportChange ToResponse() => new(Revision, ReportDocument.Utc(ChangedAt), Actor, Status, Tags, Resolution);
}
