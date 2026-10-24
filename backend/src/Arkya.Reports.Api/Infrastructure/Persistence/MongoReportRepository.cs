using System.Text.RegularExpressions;
using Arkya.Reports.Api.Application;
using Arkya.ServiceDefaults;
using Arkya.ServiceDefaults.Reports;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Arkya.Reports.Api.Infrastructure.Persistence;

public sealed class MongoReportRepository(IMongoClient client, IOptions<MongoOptions> options) : IReportRepository
{
    private readonly IMongoCollection<ReportDocument> reports = client.GetDatabase(options.Value.DatabaseName)
        .GetCollection<ReportDocument>("bug_reports");
    private static readonly ProjectionDefinition<ReportDocument, ReportDocument> WithoutImage =
        Builders<ReportDocument>.Projection.Exclude(document => document.ScreenshotBytes);
    private static readonly ProjectionDefinition<ReportDocument, ReportDocument> Summary =
        Builders<ReportDocument>.Projection.Exclude(document => document.ScreenshotBytes).Exclude(document => document.History);

    public Task InitializeAsync(CancellationToken cancellationToken) => reports.Indexes.CreateManyAsync([
        new(Builders<ReportDocument>.IndexKeys.Ascending(document => document.AccountId).Ascending(document => document.ClientReportId),
            new CreateIndexOptions { Name = "ux_report_account_client_id", Unique = true }),
        new(Builders<ReportDocument>.IndexKeys.Ascending(document => document.AccountId).Descending(document => document.CreatedAt)
                .Descending(document => document.Id), new CreateIndexOptions { Name = "ix_report_owner_created" }),
        new(Builders<ReportDocument>.IndexKeys.Ascending(document => document.Tags).Ascending(document => document.Status)
                .Descending(document => document.CreatedAt).Descending(document => document.Id),
            new CreateIndexOptions { Name = "ix_report_tag_status_created" }),
        new(Builders<ReportDocument>.IndexKeys.Ascending(document => document.Status).Descending(document => document.CreatedAt)
                .Descending(document => document.Id), new CreateIndexOptions { Name = "ix_report_status_created" }),
        new(Builders<ReportDocument>.IndexKeys.Descending(document => document.CreatedAt).Descending(document => document.Id),
            new CreateIndexOptions { Name = "ix_report_created" }),
        new(Builders<ReportDocument>.IndexKeys.Ascending(document => document.Kind).Descending(document => document.CreatedAt)
                .Descending(document => document.Id), new CreateIndexOptions { Name = "ix_report_kind_created" }),
        new(Builders<ReportDocument>.IndexKeys.Ascending(document => document.AccountId).Ascending(document => document.Kind)
                .Descending(document => document.CreatedAt).Descending(document => document.Id),
            new CreateIndexOptions { Name = "ix_report_owner_kind_created" }),
        new(Builders<ReportDocument>.IndexKeys.Ascending(document => document.Kind).Descending(document => document.UpdatedAt)
                .Descending(document => document.Id), new CreateIndexOptions { Name = "ix_report_kind_updated" }),
        new(Builders<ReportDocument>.IndexKeys.Descending(document => document.UpdatedAt).Descending(document => document.Id),
            new CreateIndexOptions { Name = "ix_report_updated" })
    ], cancellationToken);

    public async Task<ReportInsertResult> InsertAsync(StoredReport report, CancellationToken cancellationToken)
    {
        try
        {
            // One bounded document makes the report and its screenshot atomic on standalone MongoDB.
            await reports.InsertOneAsync(ReportDocument.From(report), cancellationToken: cancellationToken);
            return new(report, true);
        }
        catch (MongoWriteException exception) when (exception.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            var existing = await reports.Find(document => document.AccountId == report.Report.AccountId.ToString("D") &&
                    document.ClientReportId == report.Report.ClientReportId.ToString("D"))
                .Project(WithoutImage).FirstOrDefaultAsync(cancellationToken);
            if (existing is null) throw;
            return new(new(existing.ToResponse(), existing.PayloadHash, []), false);
        }
    }

    public async Task<BugReportResponse?> FindAsync(Guid id, Guid? accountId, CancellationToken cancellationToken)
    {
        var filter = Builders<ReportDocument>.Filter.Eq(document => document.Id, id.ToString("D"));
        if (accountId is not null) filter &= Builders<ReportDocument>.Filter.Eq(document => document.AccountId, accountId.Value.ToString("D"));
        var document = await reports.Find(filter).Project(WithoutImage).FirstOrDefaultAsync(cancellationToken);
        return document?.ToResponse();
    }

    public async Task<BugReportListResponse> ListAsync(Guid? accountId, string? tag, string? status, int offset, int limit,
        CancellationToken cancellationToken, string? kind = null, BugReportListFilters? filters = null)
    {
        var filter = Builders<ReportDocument>.Filter.Empty;
        if (accountId is not null) filter &= Builders<ReportDocument>.Filter.Eq(document => document.AccountId, accountId.Value.ToString("D"));
        if (tag is not null) filter &= Builders<ReportDocument>.Filter.AnyEq(document => document.Tags, tag);
        if (status is not null) filter &= Builders<ReportDocument>.Filter.Eq(document => document.Status, status);
        if (kind is not null)
        {
            var matchingKind = Builders<ReportDocument>.Filter.Eq(document => document.Kind, kind);
            // Legacy documents predate suggestions and are always bugs.
            if (kind == BugReportKinds.Bug)
                matchingKind |= Builders<ReportDocument>.Filter.Exists(document => document.Kind, false);
            filter &= matchingKind;
        }
        filters ??= new();
        if (filters.Search is { } search)
        {
            var literal = new BsonRegularExpression(Regex.Escape(search), "i");
            filter &= Builders<ReportDocument>.Filter.Regex(document => document.Title, literal) |
                Builders<ReportDocument>.Filter.Regex(document => document.Description, literal) |
                Builders<ReportDocument>.Filter.Regex(document => document.Id, literal);
        }
        if (filters.Scene is { } scene)
            filter &= Builders<ReportDocument>.Filter.Regex(document => document.Scene, Exact(scene));
        if (filters.ClientVersion is { } version)
            filter &= Builders<ReportDocument>.Filter.Regex(document => document.ClientVersion, Exact(version));
        if (filters.Platform is { } platform)
            filter &= Builders<ReportDocument>.Filter.Regex(document => document.Platform, Exact(platform));
        if (filters.CreatedFrom is { } from)
            filter &= Builders<ReportDocument>.Filter.Gte(document => document.CreatedAt, from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        // The exclusive next-day boundary includes the complete UTC date at MongoDB's millisecond precision.
        if (filters.CreatedTo is { } to && to < DateOnly.MaxValue)
            filter &= Builders<ReportDocument>.Filter.Lt(document => document.CreatedAt, to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var sort = filters.Sort switch
        {
            BugReportSorts.Oldest => Builders<ReportDocument>.Sort.Ascending(document => document.CreatedAt).Ascending(document => document.Id),
            BugReportSorts.Updated => Builders<ReportDocument>.Sort.Descending(document => document.UpdatedAt).Descending(document => document.Id),
            _ => Builders<ReportDocument>.Sort.Descending(document => document.CreatedAt).Descending(document => document.Id)
        };
        var total = await reports.CountDocumentsAsync(filter, cancellationToken: cancellationToken);
        var documents = await reports.Find(filter).Sort(sort)
            .Skip(offset).Limit(limit).Project(Summary).ToListAsync(cancellationToken);
        return new(documents.Select(document => document.ToResponse()).ToArray(), offset, limit, total);
    }

    private static BsonRegularExpression Exact(string value) => new("\\A" + Regex.Escape(value) + "\\z", "i");

    public async Task<byte[]?> ScreenshotAsync(Guid id, CancellationToken cancellationToken) => await reports
        .Find(document => document.Id == id.ToString("D")).Project(document => document.ScreenshotBytes)
        .FirstOrDefaultAsync(cancellationToken);

    public async Task<BugReportResponse?> UpdateAsync(Guid id, long expectedRevision, string status, string[] tags,
        string resolution, string actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var filter = Builders<ReportDocument>.Filter.Eq(document => document.Id, id.ToString("D")) &
            Builders<ReportDocument>.Filter.Eq(document => document.Revision, expectedRevision);
        var change = new ReportChangeDocument
        {
            Revision = expectedRevision + 1, ChangedAt = now.UtcDateTime, Actor = actor,
            Status = status, Tags = tags, Resolution = resolution
        };
        var update = Builders<ReportDocument>.Update.Set(document => document.Status, status)
            .Set(document => document.Tags, tags).Set(document => document.Resolution, resolution)
            .Set(document => document.UpdatedAt, now.UtcDateTime).Inc(document => document.Revision, 1)
            // Keep bounded audit context; even maximum-size UTF-8 strings remain well below BSON's 16 MiB limit.
            .PushEach(document => document.History, [change], slice: -50);
        var document = await reports.FindOneAndUpdateAsync(filter, update,
            new FindOneAndUpdateOptions<ReportDocument> { ReturnDocument = ReturnDocument.After, Projection = WithoutImage }, cancellationToken);
        return document?.ToResponse();
    }
}
