using Arkya.Reports.Api.Application;
using Arkya.Reports.Api.Domain;
using Arkya.ServiceDefaults.Reports;

namespace Arkya.Reports.Tests;

public sealed class ReportValidationTests
{
    [Fact]
    public async Task TextLimitsNullsControlsAndMissingClientIdAreRejectedBeforePersistence()
    {
        var original = Fixtures.Request();
        var invalid = new[]
        {
            original with { ClientReportId = Guid.Empty }, original with { Title = null }, original with { Title = " " },
            original with { Title = new string('a', 121) }, original with { Description = null },
            original with { Description = new string('a', 4001) }, original with { Description = "invalid\0" },
            original with { ClientVersion = null }, original with { ClientVersion = new string('v', 129) },
            original with { Scene = null }, original with { Scene = new string('s', 129) },
            original with { Platform = null }, original with { Platform = new string('p', 65) },
            original with { ScreenshotBase64 = null }, original with { ScreenshotBase64 = "malformed" }
        };
        var repository = new MemoryReportRepository();
        var service = new ReportService(repository, new FixedClock());
        foreach (var request in invalid)
            Assert.Equal(ReportChangeStatus.Invalid, (await service.SubmitAsync(StubSessions.Alice, request, default)).Status);
        Assert.Equal(0, (await repository.ListAsync(null, null, null, 0, 100, default)).Total);
    }

    [Fact]
    public async Task InvalidTriageCannotMutateStoredReport()
    {
        var repository = new MemoryReportRepository();
        var service = new ReportService(repository, new FixedClock());
        var report = (await service.SubmitAsync(StubSessions.Alice, Fixtures.Request(), default)).Report!;
        var original = new UpdateBugReportRequest(1, "triaged", [], "");
        foreach (var invalid in new[]
        {
            original with { ExpectedRevision = 0 }, original with { ExpectedRevision = long.MaxValue },
            original with { Status = null }, original with { Status = "fixed" }, original with { Tags = null },
            original with { Tags = [null!] }, original with { Tags = ["two words"] }, original with { Tags = ["-bad"] },
            original with { Tags = [new string('a', 33)] }, original with { Tags = Enumerable.Repeat("tag", 11).ToArray() },
            original with { Resolution = null }, original with { Resolution = new string('a', 4001) }
        })
            Assert.Equal(ReportChangeStatus.Invalid, (await service.UpdateAsync(report.Id, invalid, "admin", default)).Status);
        Assert.Equal(ReportChangeStatus.Invalid, (await service.UpdateAsync(report.Id, original, "spoof\r\nheader", default)).Status);
        Assert.Equal(1, (await repository.FindAsync(report.Id, null, default))!.Revision);
    }

    [Theory]
    [InlineData(1921, 1, 6, 0)]
    [InlineData(1, 1081, 6, 0)]
    [InlineData(1, 1, 3, 0)]
    [InlineData(1, 1, 6, 5)]
    public void InvalidPngDimensionsColorOrFilterFail(int width, int height, byte color, byte filter) =>
        Assert.False(PngScreenshot.TryRead(Convert.ToBase64String(Fixtures.Png(width, height, color, filter)), out _, out _));

    [Fact]
    public void PngRequiresIntactChunksCompleteRasterAndEnd()
    {
        var corrupted = Fixtures.Png();
        corrupted[20] ^= 1;
        foreach (var invalid in new[]
        {
            corrupted, Fixtures.Png(end: false), Fixtures.Png(rasterLength: 4), Fixtures.Png(rasterLength: 6),
            Fixtures.Png().Concat(new byte[] { 0 }).ToArray(), new byte[100]
        })
            Assert.False(PngScreenshot.TryRead(Convert.ToBase64String(invalid), out _, out _));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(6)]
    public void ValidPngIsRetainedWithoutOptionalPrivateMetadata(byte color)
    {
        Assert.True(PngScreenshot.TryRead(Convert.ToBase64String(Fixtures.Png(color: color, metadata: true)), out var image, out var metadata));
        Assert.Equal(Fixtures.Png(color: color), image);
        Assert.Equal(1, metadata!.Width);
        Assert.Equal(1, metadata.Height);
        Assert.Equal(image!.Length, metadata.SizeBytes);
        Assert.Equal(64, metadata.Sha256.Length);
    }
}
