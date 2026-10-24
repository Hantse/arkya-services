using System.Net;
using System.Text;
using Arkya.Publications.Mcp;
using Microsoft.Extensions.Options;
using ModelContextProtocol;

namespace Arkya.Admin.Mcp.Tests;

public sealed class PublicationClientTests
{
    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.Redirect)]
    public async Task Upstream_errors_do_not_disclose_response_bodies_or_credentials(HttpStatusCode status)
    {
        var key = new string('a', 40);
        using var http = CreateHttp(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent("private-diagnostic " + key)
        })));
        var error = await Assert.ThrowsAsync<McpException>(() => Create(http).GetAsync(Guid.NewGuid(), default));
        Assert.DoesNotContain("private-diagnostic", error.Message);
        Assert.DoesNotContain(key, error.Message);
    }

    [Fact]
    public async Task Transport_errors_do_not_disclose_exception_messages_or_credentials()
    {
        using var http = CreateHttp(new Handler((_, _) => throw new HttpRequestException("private-diagnostic " + new string('a', 40))));
        var error = await Assert.ThrowsAsync<McpException>(() => Create(http).GetAsync(Guid.NewGuid(), default));
        Assert.DoesNotContain("private-diagnostic", error.Message);
        Assert.DoesNotContain(new string('a', 40), error.Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Oversized_responses_are_bounded_with_or_without_content_length(bool knownLength)
    {
        var bytes = new byte[8 * 1024 * 1024 + 1];
        using var http = CreateHttp(new Handler((_, _) =>
        {
            HttpContent content = knownLength ? new ByteArrayContent(bytes) : new StreamContent(new UnseekableStream(bytes));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }));
        var error = await Assert.ThrowsAsync<McpException>(() => Create(http).GetAsync(Guid.NewGuid(), default));
        Assert.Contains("size limit", error.Message);
    }

    [Theory]
    [InlineData("not JSON: private-diagnostic")]
    [InlineData("null")]
    public async Task Malformed_or_empty_JSON_is_reported_without_upstream_details(string body)
    {
        using var http = CreateHttp(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        })));
        var error = await Assert.ThrowsAsync<McpException>(() => Create(http).GetAsync(Guid.NewGuid(), default));
        Assert.DoesNotContain("private-diagnostic", error.Message);
    }

    [Fact]
    public async Task Slow_response_body_is_cancelled_after_the_configured_deadline()
    {
        using var http = CreateHttp(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new StalledStream())
        })));
        http.Timeout = TimeSpan.FromMilliseconds(100);
        var error = await Assert.ThrowsAsync<McpException>(() => Create(http).GetAsync(Guid.NewGuid(), default));
        Assert.Contains("timed out", error.Message);
    }

    [Fact]
    public async Task Caller_cancellation_is_preserved_while_reading_the_response_body()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = CreateHttp(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new StalledStream(started))
        })));
        using var caller = new CancellationTokenSource();
        var pending = Create(http).GetAsync(Guid.NewGuid(), caller.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        caller.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task Invalid_ids_revisions_pagination_and_content_never_make_HTTP_requests()
    {
        using var http = CreateHttp(new Handler((_, _) => throw new InvalidOperationException("Unexpected HTTP request")));
        var client = Create(http);
        var id = Guid.NewGuid();
        await Assert.ThrowsAsync<McpException>(() => client.GetAsync(Guid.Empty, default));
        await Assert.ThrowsAsync<McpException>(() => client.PublishAsync(id, 0, default));
        await Assert.ThrowsAsync<McpException>(() => client.UnpublishAsync(Guid.Empty, 1, default));
        await Assert.ThrowsAsync<McpException>(() => client.DeleteAsync(id, long.MaxValue, default));
        foreach (var filter in new[] { ("other", (string?)null, 0, 20), (null, "other", 0, 20),
            (null, null, -1, 20), (null, null, 10001, 20), (null, null, 0, 0), (null, null, 0, 51) })
            await Assert.ThrowsAsync<McpException>(() => client.ListAsync(filter.Item1, filter.Item2, filter.Item3, filter.Item4, default));
        var valid = new CreatePublicationRequest("news", "fr", "Title", "Summary", "Body\nWith a tab\t.", null);
        foreach (var invalid in new[]
        {
            valid with { Kind = "other" }, valid with { Language = "other" },
            valid with { Title = " " }, valid with { Title = "Title\n" }, valid with { Title = new string('a', 161) },
            valid with { Summary = "Summary\t" }, valid with { Summary = new string('a', 401) },
            valid with { Body = " " }, valid with { Body = "unsafe\u0001text" }, valid with { Body = new string('a', 20001) },
            valid with { GameVersion = "1.0" }, valid with { Kind = "release-note" },
            valid with { Kind = "release-note", GameVersion = "../unsafe" },
            valid with { Kind = "release-note", GameVersion = new string('a', 65) }
        })
        {
            await Assert.ThrowsAsync<McpException>(() => client.CreateAsync(invalid, default));
            await Assert.ThrowsAsync<McpException>(() => client.UpdateAsync(id,
                new UpdatePublicationRequest(1, invalid.Kind, invalid.Language, invalid.Title, invalid.Summary, invalid.Body, invalid.GameVersion), default));
        }
        await Assert.ThrowsAsync<McpException>(() => client.UpdateAsync(id, new UpdatePublicationRequest(0,
            valid.Kind, valid.Language, valid.Title, valid.Summary, valid.Body, valid.GameVersion), default));
    }

    [Theory]
    [InlineData("https://content.example.org", false, true)]
    [InlineData("http://127.0.0.1:5145", true, true)]
    [InlineData("http://localhost:5145", true, true)]
    [InlineData("http://localhost:5145", false, false)]
    [InlineData("http://content.example.org", true, false)]
    [InlineData("https://user:password@content.example.org", false, false)]
    [InlineData("https://content.example.org?key=secret", false, false)]
    [InlineData("https://content.example.org#fragment", false, false)]
    [InlineData("https://content.example.org/admin", false, false)]
    public void Operator_configuration_limits_the_destination_of_Content_credentials(string origin, bool localHttp, bool valid)
    {
        var options = new PublicationsMcpOptions
        {
            Enabled = true, BaseUrl = origin, AdminKey = new string('a', 40), AllowLoopbackHttp = localHttp
        };
        Assert.Equal(valid, options.IsValid());
    }

    [Theory]
    [InlineData(31, false)]
    [InlineData(32, true)]
    [InlineData(512, true)]
    [InlineData(513, false)]
    public void Operator_key_length_is_bounded(int length, bool valid)
    {
        Assert.Equal(valid, new PublicationsMcpOptions { BaseUrl = "https://content.test", AdminKey = new string('a', length) }.IsValid());
        Assert.False(new PublicationsMcpOptions { BaseUrl = "https://content.test", AdminKey = new string('a', 32) + "\n" }.IsValid());
    }

    private static HttpClient CreateHttp(HttpMessageHandler handler) => new(handler) { BaseAddress = new Uri("https://content.test/") };

    private static PublicationsMcpClient Create(HttpClient http) => new(http, Options.Create(new PublicationsMcpOptions
    {
        Enabled = true, BaseUrl = "https://content.test", AdminKey = new string('a', 40)
    }));

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            response(request, cancellationToken);
    }

    private sealed class UnseekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }

    private sealed class StalledStream(TaskCompletionSource? started = null) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            started?.TrySetResult();
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
