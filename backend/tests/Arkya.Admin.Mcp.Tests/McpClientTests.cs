using System.Net;
using Microsoft.Extensions.Options;
using ModelContextProtocol;

namespace Arkya.Admin.Mcp.Tests;

public sealed class McpClientTests
{
    [Fact]
    public async Task Oversized_screenshot_is_rejected_without_returning_image()
    {
        using var http = new HttpClient(new Handler(() =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[2 * 1024 * 1024 + 1]) };
            response.Content.Headers.ContentType = new("image/png");
            return response;
        })) { BaseAddress = new Uri("https://reports.test/") };
        var client = Create(http);
        await Assert.ThrowsAsync<McpException>(() => client.ScreenshotAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Slow_body_is_cancelled_after_the_configured_deadline()
    {
        using var http = new HttpClient(new Handler(() =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StalledStream()) };
            response.Content.Headers.ContentType = new("image/png");
            return response;
        })) { BaseAddress = new Uri("https://reports.test/"), Timeout = TimeSpan.FromMilliseconds(100) };
        var error = await Assert.ThrowsAsync<McpException>(() => Create(http).ScreenshotAsync(Guid.NewGuid(), default));
        Assert.Contains("timed out", error.Message);
    }

    [Fact]
    public async Task Non_png_content_is_not_returned_as_an_image()
    {
        using var http = new HttpClient(new Handler(() =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>error</html>") };
            response.Content.Headers.ContentType = new("image/png");
            return response;
        })) { BaseAddress = new Uri("https://reports.test/") };
        await Assert.ThrowsAsync<McpException>(() => Create(http).ScreenshotAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Remote_errors_do_not_disclose_response_or_credentials()
    {
        using var http = new HttpClient(new Handler(() => new(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("private-diagnostic-or-secret")
        })) { BaseAddress = new Uri("https://reports.test/") };
        var error = await Assert.ThrowsAsync<McpException>(() => Create(http).GetAsync(Guid.NewGuid(), default));
        Assert.DoesNotContain("private-diagnostic", error.Message);
        Assert.Contains("refused", error.Message);
    }

    [Fact]
    public async Task Invalid_id_and_unbounded_pagination_never_make_http_requests()
    {
        using var http = new HttpClient(new Handler(() => throw new InvalidOperationException("Unexpected HTTP request")));
        var client = Create(http);
        await Assert.ThrowsAsync<McpException>(() => client.GetAsync(Guid.Empty, default));
        await Assert.ThrowsAsync<McpException>(() => client.ListAsync(null, null, 0, 1000, default));
        await Assert.ThrowsAsync<McpException>(() => client.ListAsync(null, null, 0, 20, default, "unknown"));
    }

    private static ReportsMcpClient Create(HttpClient client) => new(client, Options.Create(new ReportsMcpOptions { AdminKey = new string('a', 32) }));

    private sealed class Handler(Func<HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response());
    }

    private sealed class StalledStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
