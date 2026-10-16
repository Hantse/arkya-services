using System.Net;
using System.Net.Http.Json;
using Arkya.Characters.Api.Application;
using Arkya.Characters.Api.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace Arkya.Characters.Tests;

public sealed class IdentitySessionValidatorTests
{
    [Fact]
    public async Task UpstreamTimeoutReturnsUnavailable()
    {
        using var client = new HttpClient(new StubHandler((_, _) => throw new TaskCanceledException("Timed out.")))
        { BaseAddress = new Uri("https://identity.example/") };
        var validator = Create(client);

        var result = await validator.ValidateAsync("token", CancellationToken.None);

        Assert.Equal(SessionValidationStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task RequestCancellationPropagates()
    {
        using var cancellation = new CancellationTokenSource();
        using var client = new HttpClient(new StubHandler((_, _) =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        })) { BaseAddress = new Uri("https://identity.example/") };
        var validator = Create(client);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => validator.ValidateAsync("token", cancellation.Token));
    }

    [Fact]
    public async Task InvalidIdentityPayloadFailsClosed()
    {
        using var client = new HttpClient(new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { accountId = Guid.Empty, displayName = "Alice", expiresAt = FixedTimeProvider.Now.AddHours(1) })
        }))) { BaseAddress = new Uri("https://identity.example/") };
        var validator = Create(client);

        var result = await validator.ValidateAsync("token", CancellationToken.None);

        Assert.Equal(SessionValidationStatus.Unavailable, result.Status);
        Assert.Null(result.Session);
    }

    [Theory]
    [InlineData("http://example.com", false, false)]
    [InlineData("http://localhost:5088", false, false)]
    [InlineData("http://localhost:5088", true, true)]
    [InlineData("https://identity.example", false, true)]
    [InlineData("https://user:password@identity.example", true, false)]
    [InlineData("https://identity.example?secret=value", true, false)]
    public void IdentityTransportRequiresHttpsOutsideLocalDevelopment(string baseUrl, bool development, bool valid)
    {
        Assert.Equal(valid, IdentityOptions.HasSafeBaseUrl(new IdentityOptions { BaseUrl = baseUrl }, development));
    }

    private static IdentitySessionValidator Create(HttpClient client) => new(client,
        Options.Create(new IdentityOptions { BaseUrl = "https://identity.example/", ServiceKey = CharactersFactory.IdentityKey }),
        new FixedTimeProvider());

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}
