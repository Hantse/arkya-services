using System.Net;
using Arkya.Admin.Reports;
using Arkya.ServiceDefaults.Reports;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Arkya.Admin.Tests;

public sealed class ReportsClientTests
{
    [Fact]
    public async Task FilterValuesAreEncodedAsLiteralQueryParameters()
    {
        using var stub = new ReportsStub();
        using var client = new HttpClient(stub) { BaseAddress = new Uri("https://reports.example/") };
        var reports = new ReportsAdminClient(client, Options.Create(new ReportsApiOptions
        {
            Enabled = true, AdminKey = AdminFactory.ReportsAdminKey
        }));
        var filters = new BugReportListFilters("bridge&kind=bug", "Starter/Island?status=closed", "0.1+beta",
            "Windows & WebGL", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 24), "updated");
        await reports.ListAsync("urgent", "triaged", 40, 10, default, BugReportKinds.Suggestion, filters);
        var query = QueryHelpers.ParseQuery(Assert.Single(stub.Calls).Uri.Query);
        Assert.Equal("suggestion", Assert.Single(query["kind"]));
        Assert.Equal("triaged", Assert.Single(query["status"]));
        Assert.Equal(filters.Search, Assert.Single(query["search"]));
        Assert.Equal(filters.Scene, Assert.Single(query["scene"]));
        Assert.Equal(filters.ClientVersion, Assert.Single(query["clientVersion"]));
        Assert.Equal(filters.Platform, Assert.Single(query["platform"]));
        Assert.Equal("2026-10-01", Assert.Single(query["createdFrom"]));
        Assert.Equal("2026-10-24", Assert.Single(query["createdTo"]));
        Assert.Equal("updated", Assert.Single(query["sort"]));
    }

    [Fact]
    public async Task ScreenshotDeadlineIncludesReadingTheBodyAfterResponseHeaders()
    {
        using var client = new HttpClient(new StalledResponse())
        {
            BaseAddress = new Uri("https://reports.example/"),
            Timeout = TimeSpan.FromMilliseconds(100)
        };
        var reports = new ReportsAdminClient(client, Options.Create(new ReportsApiOptions
        {
            Enabled = true, AdminKey = AdminFactory.ReportsAdminKey
        }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reports.ScreenshotAsync(ReportsStub.Id, default));
    }

    private sealed class StalledResponse : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = new StreamContent(new StalledBody());
            content.Headers.ContentType = new("image/png");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    private sealed class StalledBody : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
