using System.Net;
using System.Text;
using System.Text.Json;
using Arkya.Identity.Api.Contracts;
using Arkya.Launcher.Core;

namespace Arkya.Launcher.Tests;

public sealed class IdentityWireContractTests
{
    private const string Token = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string AccountId = "c025ac0b-4c7a-4612-b6f7-5992444e00f0";
    private const string ChallengeId = "22222222222222222222222222222222";
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task LoginAcceptsTheMongoIdentityCanonicalDashedIdFromExchangeAndActualMeContract()
    {
        var requests = new List<string>();
        using var http = new HttpClient(new Handler(async request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            requests.Add(path);
            if (path == "/v1/auth/discord/challenges")
            {
                var submitted = JsonSerializer.Deserialize<ChallengeRequest>(await request.Content!.ReadAsStringAsync(), WireJson)!;
                Assert.Matches("^[A-Za-z0-9_-]{43}$", submitted.CodeChallenge!);
                Assert.Null(submitted.ReturnUrl);
                return Json(new { challengeId = ChallengeId, authorizationUrl = LauncherIdentityClient.TrustedOrigin + "/v1/auth/discord/authorize/" + ChallengeId, expiresAt = Now.UtcDateTime.AddMinutes(5), pollIntervalSeconds = 5 });
            }
            if (path == "/v1/auth/discord/exchange")
            {
                var submitted = JsonSerializer.Deserialize<ExchangeRequest>(await request.Content!.ReadAsStringAsync(), WireJson)!;
                Assert.Equal(ChallengeId, submitted.ChallengeId);
                Assert.Matches("^[A-Za-z0-9_-]{43}$", submitted.CodeVerifier!);
                Assert.False(submitted.UseRefreshCookie);
                // IdentityService.SessionResult returns the stored Mongo account ID unchanged, as D format.
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        {"accountId":"c025ac0b-4c7a-4612-b6f7-5992444e00f0","displayName":"Discord name","sessionToken":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA","expiresAt":"2026-10-24T02:00:00Z"}
                        """, Encoding.UTF8, "application/json")
                };
            }
            Assert.Equal("/v1/auth/me", path);
            Assert.Equal(Token, request.Headers.Authorization!.Parameter);
            // Compile the real backend DTO into these tests so property changes affect the wire fixture.
            return Json(new AccountSessionResponse(AccountId, "Verified Discord name", Now.UtcDateTime.AddHours(8), HasBetaAccess: true));
        }));
        var session = await new LauncherIdentityClient(http, new Clock()).LoginWithDiscordAsync(_ => { }, CancellationToken.None);
        Assert.Equal(AccountId, session.AccountId);
        Assert.Equal("Verified Discord name", session.DisplayName);
        Assert.Equal(Now.AddHours(8), session.ExpiresAt);
        Assert.Equal(new[] { "/v1/auth/discord/challenges", "/v1/auth/discord/exchange", "/v1/auth/me" }, requests);
    }

    [Theory]
    [InlineData("c025ac0b-4c7a-4612-b6f7-5992444e00f0")]
    [InlineData("c025ac0b4c7a4612b6f75992444e00f0")]
    public void SessionPreservesCanonicalAndLegacyAccountIdsWithoutNormalization(string accountId) =>
        Assert.Equal(accountId, new LauncherSession(Token, accountId, "Aelyra", Now.AddHours(8)).AccountId);

    [Theory]
    [InlineData("{c025ac0b-4c7a-4612-b6f7-5992444e00f0}")]
    [InlineData("(c025ac0b-4c7a-4612-b6f7-5992444e00f0)")]
    [InlineData(" c025ac0b-4c7a-4612-b6f7-5992444e00f0 ")]
    [InlineData("invalid-account")]
    public void SessionStillRejectsNonCanonicalAccountIdShapes(string accountId) =>
        Assert.Throws<LauncherAuthenticationException>(() => new LauncherSession(Token, accountId, "Aelyra", Now.AddHours(8)));

    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(value, WireJson), Encoding.UTF8, "application/json")
    };
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }
}
