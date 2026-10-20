using System.Net;
using Arkya.Admin.Accounts;
using Microsoft.Extensions.Options;

namespace Arkya.Admin.Tests;

public sealed class AccountsClientTests
{
    [Fact]
    public async Task DeadlineIncludesReadingBodyAfterReceivingHeaders()
    {
        using var client = new HttpClient(new ResponseHandler(() => new StreamContent(new StalledBody())))
        {
            BaseAddress = new("https://identity.example/"), Timeout = TimeSpan.FromMilliseconds(100)
        };
        var accounts = new IdentityAdminClient(client, Options.Create(new IdentityAdminApiOptions { Enabled = true, AdminKey = AccountsStub.Key }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => accounts.ListAsync(null, 1, 25, default));
    }

    [Fact]
    public async Task ChunkedOversizeResponseIsRejectedBeforeDeserialization()
    {
        using var client = new HttpClient(new ResponseHandler(() => new StreamContent(new NonSeekableBody(new byte[2 * 1024 * 1024 + 1]))))
        { BaseAddress = new("https://characters.example/") };
        var characters = new CharactersAdminClient(client, Options.Create(new CharactersAdminApiOptions { Enabled = true, AdminKey = AccountsStub.Key }));
        var error = await Assert.ThrowsAsync<PlayerAdminApiException>(() => characters.OverviewAsync(default));
        Assert.Equal(HttpStatusCode.BadGateway, error.StatusCode);
    }

    private sealed class ResponseHandler(Func<HttpContent> content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content() });
    }

    private sealed class NonSeekableBody(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }

    private sealed class StalledBody : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        { await Task.Delay(Timeout.InfiniteTimeSpan, ct); return 0; }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
