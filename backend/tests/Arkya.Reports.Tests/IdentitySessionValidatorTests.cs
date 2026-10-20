using System.Net;
using System.Net.Http.Json;
using Arkya.Reports.Api.Application;
using Arkya.Reports.Api.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace Arkya.Reports.Tests;

public sealed class IdentitySessionValidatorTests
{
    [Fact]
    public async Task IntrospectionAuthenticatesServiceAndRejectsExpiredOrMalformedIdentity()
    {
        var calls = 0;
        using var client = new HttpClient(new StubHandler(async request =>
        {
            calls++;
            Assert.Equal("/internal/sessions/introspect", request.RequestUri!.AbsolutePath);
            Assert.Equal(ReportsFactory.ServiceKey, request.Headers.GetValues("X-Arkya-Service-Key").Single());
            Assert.Null(request.Headers.Authorization);
            Assert.Contains("\"sessionToken\":\"opaque-token\"", await request.Content!.ReadAsStringAsync());
            return new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new PlayerSession(StubSessions.Alice, "Alice", FixedClock.Now.AddSeconds(calls == 1 ? 1 : -1)))
            };
        })) { BaseAddress = new Uri("https://identity.example") };
        var validator = Validator(client);
        Assert.Equal(SessionValidationStatus.Valid, (await validator.ValidateAsync("opaque-token", default)).Status);
        Assert.Equal(SessionValidationStatus.Invalid, (await validator.ValidateAsync("opaque-token", default)).Status);
        Assert.Equal(SessionValidationStatus.Invalid, (await validator.ValidateAsync(new string('x', 2049), default)).Status);
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(302)]
    [InlineData(403)]
    public async Task UnexpectedIdentityResponsesFailClosed(int status)
    {
        using var client = new HttpClient(new StubHandler(_ => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status))))
        { BaseAddress = new Uri("https://identity.example") };
        Assert.Equal(SessionValidationStatus.Unavailable, (await Validator(client).ValidateAsync("opaque-token", default)).Status);
    }

    [Fact]
    public async Task NetworkAndResponseReadFaultsReturnUnavailable()
    {
        foreach (var failure in new Exception[] { new HttpRequestException(), new IOException(), new TaskCanceledException() })
        {
            using var client = new HttpClient(new StubHandler(_ => throw failure)) { BaseAddress = new Uri("https://identity.example") };
            Assert.Equal(SessionValidationStatus.Unavailable, (await Validator(client).ValidateAsync("token", default)).Status);
        }
    }

    [Fact]
    public async Task InvalidSuccessfulIdentityPayloadFailsClosed()
    {
        using var client = new HttpClient(new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new PlayerSession(Guid.Empty, "Alice", FixedClock.Now.AddHours(1)))
        }))) { BaseAddress = new Uri("https://identity.example") };
        Assert.Equal(SessionValidationStatus.Unavailable, (await Validator(client).ValidateAsync("token", default)).Status);
    }

    [Theory]
    [InlineData("http://identity.arkya-beta.svc.cluster.local:8080/", false, false)]
    [InlineData("http://identity.arkya-beta.svc.cluster.local:8080/", true, true)]
    [InlineData("http://identity.arkya-beta.svc.cluster.local:99999/", true, false)]
    [InlineData("http://identity.arkya-beta.svc.cluster.local:8080/path", true, false)]
    [InlineData("http://identity.arkya-beta.svc.cluster.local.evil/", true, false)]
    [InlineData("http://user@identity.arkya-beta.svc.cluster.local/", true, false)]
    [InlineData("http://127.0.0.1/", true, false)]
    [InlineData("http://example.com/", true, false)]
    [InlineData("https://identity.example/", false, true)]
    public void ClusterHttpRequiresExplicitOptInAndAnExactKubernetesServiceOrigin(string url, bool optIn, bool expected) =>
        Assert.Equal(expected, IdentityOptions.HasSafeBaseUrl(new IdentityOptions { BaseUrl = url, AllowClusterHttp = optIn }, false));

    private static IdentitySessionValidator Validator(HttpClient client) => new(client,
        Options.Create(new IdentityOptions { BaseUrl = "https://identity.example", ServiceKey = ReportsFactory.ServiceKey }), new FixedClock());
    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> action) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => action(request);
    }
}
