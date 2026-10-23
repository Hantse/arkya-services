using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Arkya.Launcher.Core;

namespace Arkya.Launcher.Tests;

public sealed class IdentityTests
{
    private const string AccountId = "11111111111111111111111111111111";
    private const string ChallengeId = "22222222222222222222222222222222";
    private const string Token = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task LoginUsesFreshPkcePollsAndVerifiesTheAccountWithoutRefreshCookies()
    {
        string? challenge = null;
        string? verifier = null;
        var delays = new List<TimeSpan>();
        var exchanges = 0;
        var meRequests = 0;
        using var http = Client(async request =>
        {
            Assert.Equal(LauncherIdentityClient.TrustedOrigin, request.RequestUri!.GetLeftPart(UriPartial.Authority));
            Assert.False(request.Headers.Contains("Cookie"));
            var path = request.RequestUri.AbsolutePath;
            if (path == "/v1/auth/discord/challenges")
            {
                Assert.Null(request.Headers.Authorization);
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                challenge = json.RootElement.GetProperty("codeChallenge").GetString();
                Assert.Single(json.RootElement.EnumerateObject());
                Assert.Matches("^[A-Za-z0-9_-]{43}$", challenge!);
                return Json(new { challengeId = ChallengeId, authorizationUrl = AuthorizationUrl(), expiresAt = Now.AddMinutes(5) });
            }
            if (path == "/v1/auth/discord/exchange")
            {
                Assert.Null(request.Headers.Authorization);
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                var submitted = json.RootElement.GetProperty("codeVerifier").GetString();
                verifier ??= submitted;
                Assert.Equal(verifier, submitted);
                Assert.Matches("^[A-Za-z0-9_-]{43}$", verifier!);
                Assert.Equal(challenge, Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier!))));
                Assert.Equal(ChallengeId, json.RootElement.GetProperty("challengeId").GetString());
                Assert.False(json.RootElement.GetProperty("useRefreshCookie").GetBoolean());
                if (exchanges++ == 0)
                {
                    var pending = new HttpResponseMessage(HttpStatusCode.Accepted);
                    pending.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
                    return pending;
                }
                return Json(new { accountId = AccountId, displayName = "Exchange name", sessionToken = Token, expiresAt = Now.AddHours(8) });
            }
            Assert.Equal("/v1/auth/me", path);
            Assert.Equal(new AuthenticationHeaderValue("Bearer", Token), request.Headers.Authorization);
            meRequests++;
            return Json(new { accountId = AccountId, displayName = "Aelyra", expiresAt = Now.AddHours(7), isBanned = false });
        });
        var identity = new LauncherIdentityClient(http, new Clock(Now), (duration, _) => { delays.Add(duration); return Task.CompletedTask; });
        Uri? browser = null;
        var result = await identity.LoginWithDiscordAsync(uri => browser = uri, CancellationToken.None);
        Assert.Equal(AuthorizationUrl(), browser!.AbsoluteUri);
        Assert.DoesNotContain(verifier!, browser.AbsoluteUri);
        Assert.Equal("Aelyra", result.DisplayName);
        Assert.Equal(Now.AddHours(7), result.ExpiresAt);
        Assert.Equal(new[] { TimeSpan.FromSeconds(5) }, delays);
        Assert.Equal(1, meRequests);
        Assert.DoesNotContain(Token, result.ToString());
    }

    [Theory]
    [InlineData("http://api.arkya.online/v1/auth/discord/authorize/")]
    [InlineData("https://api.arkya.online.evil.test/v1/auth/discord/authorize/")]
    [InlineData("https://evil.test/v1/auth/discord/authorize/")]
    [InlineData("https://user@api.arkya.online/v1/auth/discord/authorize/")]
    [InlineData("https://api.arkya.online:8443/v1/auth/discord/authorize/")]
    [InlineData("https://api.arkya.online/other/")]
    public void RejectsUntrustedBrowserDestinations(string prefix) =>
        Assert.Throws<LauncherAuthenticationException>(() => LauncherIdentityClient.ValidateAuthorizationUrl(prefix + ChallengeId, ChallengeId));

    [Theory]
    [InlineData("?redirect=https://evil.test")]
    [InlineData("#fragment")]
    [InlineData("/extra")]
    public void RejectsModifiedAuthorizationPath(string suffix) =>
        Assert.Throws<LauncherAuthenticationException>(() => LauncherIdentityClient.ValidateAuthorizationUrl(AuthorizationUrl() + suffix, ChallengeId));

    [Fact]
    public async Task RejectedBrowserUrlNeverOpensTheBrowserOrExchangesTheProof()
    {
        var requests = 0;
        using var http = Client(_ =>
        {
            requests++;
            return Task.FromResult(Json(new { challengeId = ChallengeId, authorizationUrl = "https://evil.test/", expiresAt = Now.AddMinutes(5) }));
        });
        await Assert.ThrowsAsync<LauncherAuthenticationException>(() => new LauncherIdentityClient(http, new Clock(Now))
            .LoginWithDiscordAsync(_ => throw new InvalidOperationException("Browser must remain closed."), CancellationToken.None));
        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task PollingStopsAtChallengeExpiryAndClampsRetryAfter()
    {
        var clock = new Clock(Now);
        var requests = 0;
        var delays = new List<TimeSpan>();
        using var http = Client(request =>
        {
            requests++;
            if (request.RequestUri!.AbsolutePath.EndsWith("challenges"))
                return Task.FromResult(Json(new { challengeId = ChallengeId, authorizationUrl = AuthorizationUrl(), expiresAt = Now.AddSeconds(20) }));
            var pending = new HttpResponseMessage(HttpStatusCode.Accepted);
            pending.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromHours(1));
            return Task.FromResult(pending);
        });
        var identity = new LauncherIdentityClient(http, clock, (duration, _) =>
        {
            delays.Add(duration); clock.Now += duration; return Task.CompletedTask;
        });
        await Assert.ThrowsAsync<LauncherAuthenticationException>(() => identity.LoginWithDiscordAsync(_ => { }, CancellationToken.None));
        Assert.Equal(new[] { TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(5) }, delays);
        Assert.Equal(3, requests);
    }

    [Fact]
    public async Task ExpiredSessionCannotIssueAnAuthenticatedRequest()
    {
        using var http = Client(_ => throw new InvalidOperationException("Expired session must not be sent."));
        var error = await Assert.ThrowsAsync<LauncherAuthenticationException>(() => new LauncherIdentityClient(http, new Clock(Now))
            .VerifySessionAsync(new LauncherSession(Token, AccountId, "Aelyra", Now), CancellationToken.None));
        Assert.True(error.SessionInvalid);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task MeRejectsAnotherAccountAndBannedAccounts(bool sameAccount, bool banned)
    {
        using var http = Client(_ => Task.FromResult(Json(new
        {
            accountId = sameAccount ? AccountId : ChallengeId, displayName = "Other", expiresAt = Now.AddHours(1), isBanned = banned
        })));
        var error = await Assert.ThrowsAsync<LauncherAuthenticationException>(() => new LauncherIdentityClient(http, new Clock(Now))
            .VerifySessionAsync(Session(), CancellationToken.None));
        Assert.True(error.SessionInvalid);
    }

    [Fact]
    public async Task AuthenticatedRedirectIsRejectedWithoutFollowingIt()
    {
        var requests = 0;
        using var http = Client(request =>
        {
            requests++;
            Assert.Equal("/v1/auth/me", request.RequestUri!.AbsolutePath);
            var response = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect);
            response.Headers.Location = new Uri("https://evil.test/");
            return Task.FromResult(response);
        });
        await Assert.ThrowsAsync<LauncherAuthenticationException>(() => new LauncherIdentityClient(http, new Clock(Now))
            .VerifySessionAsync(Session(), CancellationToken.None));
        Assert.Equal(1, requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task RejectedSessionIsMarkedInvalidWithoutEchoingResponse(HttpStatusCode status)
    {
        using var http = Client(_ => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(Token) }));
        var error = await Assert.ThrowsAsync<LauncherAuthenticationException>(() => new LauncherIdentityClient(http, new Clock(Now))
            .VerifySessionAsync(Session(), CancellationToken.None));
        Assert.True(error.SessionInvalid);
        Assert.DoesNotContain(Token, error.ToString());
    }

    [Fact]
    public async Task MalformedResponseDoesNotExposeItsBody()
    {
        using var http = Client(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("broken-" + Token) }));
        var error = await Assert.ThrowsAsync<LauncherAuthenticationException>(() => new LauncherIdentityClient(http, new Clock(Now))
            .VerifySessionAsync(Session(), CancellationToken.None));
        Assert.DoesNotContain(Token, error.ToString());
    }

    [Fact]
    public async Task LogoutRevokesTheSameBearerAtTrustedOrigin()
    {
        using var http = Client(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(LauncherIdentityClient.TrustedOrigin + "/v1/auth/logout", request.RequestUri!.AbsoluteUri);
            Assert.Equal(Token, request.Headers.Authorization!.Parameter);
            Assert.Null(request.Content);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        });
        await new LauncherIdentityClient(http).LogoutAsync(Session(), CancellationToken.None);
    }

    [Fact]
    public async Task CancellationAtTheEndOfMeResponseCannotCommitASession()
    {
        using var cancellation = new CancellationTokenSource();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { accountId = AccountId, displayName = "Aelyra", expiresAt = Now.AddHours(1) });
        using var http = Client(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new CancellingEofStream(bytes, cancellation))
        }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new LauncherIdentityClient(http, new Clock(Now))
            .VerifySessionAsync(Session(), cancellation.Token));
    }

    [Fact]
    public async Task CancelledPollingNeverExchangesAgainOrReturnsASession()
    {
        using var cancellation = new CancellationTokenSource();
        var exchanges = 0;
        using var http = Client(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("challenges"))
                return Task.FromResult(Json(new { challengeId = ChallengeId, authorizationUrl = AuthorizationUrl(), expiresAt = Now.AddMinutes(5) }));
            exchanges++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted));
        });
        var identity = new LauncherIdentityClient(http, new Clock(Now), (_, token) =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => identity.LoginWithDiscordAsync(_ => { }, cancellation.Token));
        Assert.Equal(1, exchanges);
    }

    [Fact]
    public async Task ExpiredChallengeCannotOpenTheBrowser()
    {
        using var http = Client(_ => Task.FromResult(Json(new
        {
            challengeId = ChallengeId, authorizationUrl = AuthorizationUrl(), expiresAt = Now
        })));
        await Assert.ThrowsAsync<LauncherAuthenticationException>(() => new LauncherIdentityClient(http, new Clock(Now))
            .LoginWithDiscordAsync(_ => throw new InvalidOperationException("Browser must remain closed."), CancellationToken.None));
    }

    [Fact]
    public async Task EachLoginAttemptUsesADifferentPkceProof()
    {
        var proofs = new List<string>();
        using var cancellation = new CancellationTokenSource();
        using var http = Client(async request =>
        {
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            proofs.Add(json.RootElement.GetProperty("codeChallenge").GetString()!);
            return Json(new { challengeId = ChallengeId, authorizationUrl = AuthorizationUrl(), expiresAt = Now });
        });
        var identity = new LauncherIdentityClient(http, new Clock(Now));
        for (var attempt = 0; attempt < 2; attempt++)
            await Assert.ThrowsAsync<LauncherAuthenticationException>(() => identity.LoginWithDiscordAsync(_ => { }, cancellation.Token));
        Assert.Equal(2, proofs.Distinct().Count());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OversizedResponsesAreRejectedWithoutExposingTheirBody(bool declaredLength)
    {
        var bytes = Encoding.UTF8.GetBytes(new string('x', 16385) + Token);
        using var http = Client(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = declaredLength ? new ByteArrayContent(bytes) : new StreamContent(new UnseekableStream(bytes))
        }));
        var error = await Assert.ThrowsAsync<LauncherAuthenticationException>(() => new LauncherIdentityClient(http, new Clock(Now))
            .VerifySessionAsync(Session(), CancellationToken.None));
        Assert.DoesNotContain(Token, error.ToString());
    }

    [Fact]
    public async Task AlreadyRevokedSessionCanBeLoggedOut()
    {
        using var http = Client(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)));
        await new LauncherIdentityClient(http).LogoutAsync(Session(), CancellationToken.None);
    }

    [Fact]
    public void SessionCannotLeakBearerThroughDefaultSerialization()
    {
        Assert.DoesNotContain(Token, JsonSerializer.Serialize(Session()));
        Assert.DoesNotContain(Token, Session().ToString());
    }

    private sealed class UnseekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }

    private static LauncherSession Session() => new(Token, AccountId, "Aelyra", Now.AddHours(1));
    private static string AuthorizationUrl() => LauncherIdentityClient.TrustedOrigin + "/v1/auth/discord/authorize/" + ChallengeId;
    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
    private static HttpClient Client(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) => new(new Handler(respond));
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return respond(request); }
    }
    private sealed class CancellingEofStream(byte[] bytes, CancellationTokenSource cancellation) : MemoryStream(bytes)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            var read = await base.ReadAsync(buffer, token);
            if (read == 0) cancellation.Cancel();
            return read;
        }
    }
}
