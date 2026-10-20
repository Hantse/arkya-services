using Arkya.ServiceDefaults.Reports;

namespace Arkya.Reports.Api.Application;

public sealed record StoredReport(BugReportResponse Report, string PayloadHash, byte[] Screenshot);
public sealed record ReportInsertResult(StoredReport Report, bool Created);

public interface IReportRepository
{
    Task<ReportInsertResult> InsertAsync(StoredReport report, CancellationToken cancellationToken);
    Task<BugReportResponse?> FindAsync(Guid id, Guid? accountId, CancellationToken cancellationToken);
    Task<BugReportListResponse> ListAsync(Guid? accountId, string? tag, string? status, int offset, int limit,
        CancellationToken cancellationToken, string? kind = null);
    Task<byte[]?> ScreenshotAsync(Guid id, CancellationToken cancellationToken);
    Task<BugReportResponse?> UpdateAsync(Guid id, long expectedRevision, string status, string[] tags,
        string resolution, string actor, DateTimeOffset now, CancellationToken cancellationToken);
}
