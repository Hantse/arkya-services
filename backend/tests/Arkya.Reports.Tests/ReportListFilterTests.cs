using System.Net;
using System.Net.Http.Json;
using Arkya.Reports.Api.Application;
using Arkya.ServiceDefaults.Reports;
using Microsoft.Extensions.DependencyInjection;

namespace Arkya.Reports.Tests;

public sealed class ReportListFilterTests
{
    [Theory]
    [InlineData("search=")]
    [InlineData("search=%20%20")]
    [InlineData("search=hello%0Aworld")]
    [InlineData("search=a&search=b")]
    [InlineData("scene=")]
    [InlineData("clientVersion=%09")]
    [InlineData("platform=%7F")]
    [InlineData("createdFrom=2026-02-30")]
    [InlineData("createdFrom=2026-1-01")]
    [InlineData("createdFrom=2026-10-20T00%3A00%3A00Z")]
    [InlineData("createdTo=")]
    [InlineData("createdFrom=2026-10-21&createdTo=2026-10-20")]
    [InlineData("createdFrom=2026-10-20&createdFrom=2026-10-21")]
    [InlineData("sort=Newest")]
    [InlineData("sort=")]
    [InlineData("sort=unknown")]
    [InlineData("sort=newest&sort=oldest")]
    public async Task InvalidFiltersFailBeforeReadingReports(string query)
    {
        using var factory = new ReportsFactory();
        using var admin = factory.Client(admin: true);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/internal/bug-reports?" + query)).StatusCode);
    }

    [Theory]
    [InlineData("search", 120)]
    [InlineData("scene", 128)]
    [InlineData("clientVersion", 64)]
    [InlineData("platform", 64)]
    public async Task TextFiltersAcceptTheirLimitAndRejectLongerValues(string field, int maximum)
    {
        using var factory = new ReportsFactory();
        using var admin = factory.Client(admin: true);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/internal/bug-reports?{field}={new string('a', maximum)}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync($"/internal/bug-reports?{field}={new string('a', maximum + 1)}")).StatusCode);
    }

    [Theory]
    [InlineData("search=port")]
    [InlineData("scene=StarterIsland")]
    [InlineData("clientVersion=test-v1")]
    [InlineData("platform=WindowsPlayer")]
    [InlineData("createdFrom=2026-10-20")]
    [InlineData("createdTo=2026-10-20")]
    [InlineData("sort=oldest")]
    public async Task ExtendedFiltersRemainAdminOnly(string query)
    {
        using var factory = new ReportsFactory();
        using var player = factory.Client("alice");
        Assert.Equal(HttpStatusCode.BadRequest, (await player.GetAsync("/v1/bug-reports?" + query)).StatusCode);
    }

    [Fact]
    public async Task AdminFiltersComposeAndTotalDescribesTheCompleteFilteredSet()
    {
        using var factory = new ReportsFactory();
        using var admin = factory.Client(admin: true);
        var repository = factory.Services.GetRequiredService<IReportRepository>();
        var original = Fixtures.StoredReport(Guid.NewGuid(), new(2026, 10, 20, 23, 59, 59, TimeSpan.Zero));
        var report = original.Report with
        {
            Title = "Collision [port]", Kind = BugReportKinds.Suggestion,
            Status = BugReportStatuses.Triaged, Tags = ["urgent"]
        };
        await repository.InsertAsync(original with { Report = report }, default);
        await repository.InsertAsync(original with { Report = report with { Id = Guid.NewGuid(), ClientReportId = Guid.NewGuid(), Description = "Second result" } }, default);
        var excluded = new BugReportResponse[]
        {
            report with { Kind = BugReportKinds.Bug }, report with { Status = BugReportStatuses.New }, report with { Tags = [] },
            report with { Scene = "StarterIsland2" }, report with { ClientVersion = "test-v10" }, report with { Platform = "WindowsPlayer2" },
            report with { CreatedAt = new(2026, 10, 21, 0, 0, 0, TimeSpan.Zero) }, report with { Title = "Collision port" }
        };
        foreach (var item in excluded)
            await repository.InsertAsync(original with { Report = item with { Id = Guid.NewGuid(), ClientReportId = Guid.NewGuid() } }, default);

        var result = (await admin.GetFromJsonAsync<BugReportListResponse>("/internal/bug-reports?kind=suggestion&status=triaged&tag=URGENT" +
            "&search=%20%5BPORT%5D%20&scene=%20starterisland%20&clientVersion=TEST-V1&platform=windowsplayer" +
            "&createdFrom=2026-10-20&createdTo=2026-10-20&offset=1&limit=1"))!;
        Assert.Equal(2, result.Total);
        Assert.Equal(1, result.Offset);
        Assert.Equal(1, result.Limit);
        Assert.Equal("Collision [port]", Assert.Single(result.Items).Title);
        Assert.Empty((await admin.GetFromJsonAsync<BugReportListResponse>("/internal/bug-reports?search=%5E.%2A%24"))!.Items);
        Assert.Equal(report.Id, Assert.Single((await admin.GetFromJsonAsync<BugReportListResponse>($"/internal/bug-reports?search={report.Id:D}"))!.Items).Id);
    }

    [Fact]
    public async Task SortsAndPaginationRemainDeterministicWhenTimestampsMatch()
    {
        using var factory = new ReportsFactory();
        using var admin = factory.Client(admin: true);
        var repository = factory.Services.GetRequiredService<IReportRepository>();
        var timestamp = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        var records = new[]
        {
            Fixtures.StoredReport(Guid.Parse("10000000-0000-0000-0000-000000000000"), timestamp.AddDays(-1), timestamp.AddDays(2)),
            Fixtures.StoredReport(Guid.Parse("20000000-0000-0000-0000-000000000000"), timestamp),
            Fixtures.StoredReport(Guid.Parse("30000000-0000-0000-0000-000000000000"), timestamp)
        };
        foreach (var record in records) await repository.InsertAsync(record, default);
        Assert.Equal(records.Reverse().Select(item => item.Report.Id),
            (await admin.GetFromJsonAsync<BugReportListResponse>("/internal/bug-reports"))!.Items.Select(item => item.Id));
        Assert.Equal(records.Select(item => item.Report.Id),
            (await admin.GetFromJsonAsync<BugReportListResponse>("/internal/bug-reports?sort=oldest"))!.Items.Select(item => item.Id));
        var first = (await admin.GetFromJsonAsync<BugReportListResponse>("/internal/bug-reports?sort=updated&limit=1"))!;
        var second = (await admin.GetFromJsonAsync<BugReportListResponse>("/internal/bug-reports?sort=updated&offset=1&limit=1"))!;
        Assert.Equal(records[0].Report.Id, Assert.Single(first.Items).Id);
        Assert.Equal(records[2].Report.Id, Assert.Single(second.Items).Id);
        Assert.Equal(3, first.Total);
        Assert.Equal(first.Total, second.Total);
        var beyond = (await admin.GetFromJsonAsync<BugReportListResponse>("/internal/bug-reports?offset=10&limit=1"))!;
        Assert.Empty(beyond.Items);
        Assert.Equal(3, beyond.Total);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/internal/bug-reports?createdFrom=0001-01-01&createdTo=9999-12-31")).StatusCode);
    }
}
