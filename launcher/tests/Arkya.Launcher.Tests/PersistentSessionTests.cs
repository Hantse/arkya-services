using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Arkya.Launcher.Core;

namespace Arkya.Launcher.Tests;

public sealed class PersistentSessionTests : IDisposable
{
    private const string AccountId = "11111111-1111-1111-1111-111111111111";
    private const string Token = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string NextToken = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
    private const string ChallengeId = "22222222222222222222222222222222";
    private const string Refresh = ChallengeId + "." + Token;
    private const string NextRefresh = ChallengeId + "." + NextToken;
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 18, 0, 0, TimeSpan.Zero);
    private readonly string directory = Path.Combine(Path.GetTempPath(), "arkya-session-tests-" + Guid.NewGuid().ToString("N"));
    private ProtectedLauncherSessionStore Store => new(directory);

    [Fact]
    public async Task RestartRestoresThroughNativeRotationWithoutPersistingTheAccessBearer()
    {
        if (!OperatingSystem.IsWindows()) return;
        Store.Save(Credential());
        var calls = new List<string>();
        using var http = Client(async request =>
        {
            calls.Add(request.RequestUri!.AbsolutePath);
            Assert.False(request.Headers.Contains("Cookie"));
            Assert.False(request.Headers.Contains("Origin"));
            if (request.RequestUri.AbsolutePath == "/v1/auth/refresh/native")
            {
                Assert.Null(request.Headers.Authorization);
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                Assert.Equal(Refresh, body.RootElement.GetProperty("refreshToken").GetString());
                Assert.Single(body.RootElement.EnumerateObject());
                return await Exchange();
            }
            Assert.Equal(NextToken, request.Headers.Authorization!.Parameter);
            return await Me();
        });
        var identity = Identity(http);
        var restored = await identity.RestoreSessionAsync(default);
        Assert.Equal("Verified name", restored!.DisplayName);
        Assert.Equal(new[] { "/v1/auth/refresh/native", "/v1/auth/me" }, calls);
        var encrypted = File.ReadAllBytes(Path.Combine(directory, "launcher-session.dat"));
        Assert.DoesNotContain(Refresh, Encoding.UTF8.GetString(encrypted));
        Assert.DoesNotContain(NextRefresh, Encoding.UTF8.GetString(encrypted));
        var decrypted = ProtectedData.Unprotect(encrypted, Encoding.UTF8.GetBytes("Arkya.Launcher.Session.v1"), DataProtectionScope.CurrentUser);
        try
        {
            using var saved = JsonDocument.Parse(decrypted);
            Assert.Equal(NextRefresh, saved.RootElement.GetProperty("refreshToken").GetString());
            Assert.False(saved.RootElement.TryGetProperty("sessionToken", out _));
            Assert.False(saved.RootElement.TryGetProperty("displayName", out _));
        }
        finally { CryptographicOperations.ZeroMemory(decrypted); }
        Assert.DoesNotContain(Refresh, JsonSerializer.Serialize(Credential()));
        Assert.DoesNotContain(Refresh, Credential().ToString());
    }

    [Fact]
    public async Task LoginOptsIntoNativeRefreshAndPersistsOnlyAfterAccountVerification()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var http = Client(async request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("challenges")) return await Json(new
            {
                challengeId = ChallengeId, authorizationUrl = LauncherIdentityClient.TrustedOrigin + "/v1/auth/discord/authorize/" + ChallengeId,
                expiresAt = Now.AddMinutes(5)
            });
            if (request.RequestUri.AbsolutePath.EndsWith("exchange"))
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                Assert.True(body.RootElement.GetProperty("useRefreshToken").GetBoolean());
                Assert.False(body.RootElement.GetProperty("useRefreshCookie").GetBoolean());
                return await Exchange();
            }
            Assert.Null(Store.Load());
            return await Me();
        });
        var session = await Identity(http).LoginWithDiscordAsync(_ => { }, default);
        Assert.Equal("Verified name", session.DisplayName);
        Assert.NotNull(Store.Load());
    }

    [Fact]
    public async Task TamperedProtectedSessionIsRemovedWithoutCallingIdentity()
    {
        if (!OperatingSystem.IsWindows()) return;
        Store.Save(Credential());
        File.WriteAllText(Path.Combine(directory, "launcher-session.dat"), "corrupt-" + Refresh);
        using var http = Client(_ => throw new InvalidOperationException("Corrupt session must not be sent."));
        Assert.Null(await Identity(http).RestoreSessionAsync(default));
        Assert.False(File.Exists(Path.Combine(directory, "launcher-session.dat")));
    }

    [Fact]
    public async Task AbsoluteExpiryIsClearedWithoutSendingTheExpiredSecret()
    {
        if (!OperatingSystem.IsWindows()) return;
        Store.Save(new LauncherRefreshCredential(Refresh, AccountId, Now));
        using var http = Client(_ => throw new InvalidOperationException("Expired credential must not be sent."));
        Assert.Null(await Identity(http).RestoreSessionAsync(default));
        Assert.Null(Store.Load());
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task RevocationOrBanClearsThePersistedSession(HttpStatusCode status)
    {
        if (!OperatingSystem.IsWindows()) return;
        Store.Save(Credential());
        using var http = Client(_ => Task.FromResult(new HttpResponseMessage(status)));
        Assert.Null(await Identity(http).RestoreSessionAsync(default));
        Assert.Null(Store.Load());
    }

    [Fact]
    public async Task TemporaryNetworkFailurePreservesTheCredentialForRetry()
    {
        if (!OperatingSystem.IsWindows()) return;
        Store.Save(Credential());
        using var http = Client(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        var error = await Assert.ThrowsAsync<LauncherAuthenticationException>(() => Identity(http).RestoreSessionAsync(default));
        Assert.False(error.SessionInvalid);
        Assert.NotNull(Store.Load());
        Assert.DoesNotContain(Refresh, error.ToString());
    }

    [Fact]
    public async Task TemporaryMeFailureStillCommitsTheRotatedSecretBeforeRetry()
    {
        if (!OperatingSystem.IsWindows()) return;
        Store.Save(Credential());
        using (var http = Client(request => request.RequestUri!.AbsolutePath.EndsWith("native") ? Exchange() : Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))))
            await Assert.ThrowsAsync<LauncherAuthenticationException>(() => Identity(http).RestoreSessionAsync(default));
        using var retryHttp = Client(async request =>
        {
            if (!request.RequestUri!.AbsolutePath.EndsWith("native")) return await Me();
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal(NextRefresh, body.RootElement.GetProperty("refreshToken").GetString());
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        });
        Assert.Null(await Identity(retryHttp).RestoreSessionAsync(default));
    }

    [Fact]
    public async Task ClosingDuringRotationCommitsTheSuccessorBeforeTheShutdownBarrierCompletes()
    {
        if (!OperatingSystem.IsWindows()) return;
        Store.Save(Credential());
        using var cancellation = new CancellationTokenSource();
        var sent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reply = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = Client(async request =>
        {
            Assert.Equal("/v1/auth/refresh/native", request.RequestUri!.AbsolutePath);
            sent.SetResult();
            await reply.Task;
            return await Exchange();
        });
        var identity = Identity(http);
        var restoration = identity.RestoreSessionAsync(cancellation.Token);
        await sent.Task;
        cancellation.Cancel();
        var shutdown = identity.WaitForSessionOperationsAsync();
        Assert.False(shutdown.IsCompleted);
        reply.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => restoration);
        await shutdown;
        Assert.NotNull(Store.Load());
        using var retryHttp = Client(async request =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal(NextRefresh, body.RootElement.GetProperty("refreshToken").GetString());
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        });
        Assert.Null(await Identity(retryHttp).RestoreSessionAsync(default));
    }

    [Fact]
    public async Task ConcurrentRefreshCallsShareTheCompletedRotation()
    {
        if (!OperatingSystem.IsWindows()) return;
        Store.Save(Credential());
        var rotations = 0;
        using var http = Client(request =>
        {
            if (!request.RequestUri!.AbsolutePath.EndsWith("native")) return Me();
            Interlocked.Increment(ref rotations);
            return Exchange();
        });
        var identity = Identity(http);
        var session = new LauncherSession(Token, AccountId, "Player", Now.AddSeconds(10), Credential());
        var sessions = await Task.WhenAll(identity.RefreshSessionAsync(session, default), identity.RefreshSessionAsync(session, default));
        Assert.Equal(1, rotations);
        Assert.Same(sessions[0], sessions[1]);
    }

    [Fact]
    public async Task LogoutClearsLocalSessionBeforeAnUnavailableRevocationService()
    {
        if (!OperatingSystem.IsWindows()) return;
        Store.Save(Credential());
        using var http = Client(async request =>
        {
            Assert.Null(Store.Load());
            Assert.Equal("/v1/auth/logout/native", request.RequestUri!.AbsolutePath);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal(Refresh, body.RootElement.GetProperty("refreshToken").GetString());
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        });
        var identity = Identity(http);
        var session = new LauncherSession(Token, AccountId, "Player", Now.AddHours(1), Credential());
        await Assert.ThrowsAsync<LauncherAuthenticationException>(() => identity.LogoutAsync(session, default));
        Assert.Null(Store.Load());
        await Assert.ThrowsAsync<LauncherAuthenticationException>(() => identity.RefreshSessionAsync(session, default));
        Assert.Null(Store.Load());
    }

    [Fact]
    public async Task MeBanAfterRotationFailsClosedAndClearsTheSuccessor()
    {
        if (!OperatingSystem.IsWindows()) return;
        Store.Save(Credential());
        using var http = Client(request => request.RequestUri!.AbsolutePath.EndsWith("native") ? Exchange() : Me(banned: true));
        Assert.Null(await Identity(http).RestoreSessionAsync(default));
        Assert.Null(Store.Load());
    }

    [Fact]
    public async Task ChangedAccountOrAbsoluteExpiryInRefreshResponseCannotBePersisted()
    {
        if (!OperatingSystem.IsWindows()) return;
        Store.Save(Credential());
        using var http = Client(_ => Json(new
        {
            accountId = ChallengeId, displayName = "Other", sessionToken = NextToken, expiresAt = Now.AddHours(8),
            refreshToken = NextRefresh, refreshExpiresAt = Now.AddDays(31)
        }));
        Assert.Null(await Identity(http).RestoreSessionAsync(default));
        Assert.Null(Store.Load());
    }

    [Fact]
    public async Task TwoStoreInstancesSerializeRotationsAcrossProcessesAndHonorCancellation()
    {
        using var firstLock = await Store.AcquireAsync(default);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Store.AcquireAsync(cancellation.Token));
    }

    private LauncherIdentityClient Identity(HttpClient http) => new(http, new Clock(), sessionStore: Store);
    private static LauncherRefreshCredential Credential() => new(Refresh, AccountId, Now.AddDays(30));
    private static Task<HttpResponseMessage> Exchange() => Json(new
    {
        accountId = AccountId, displayName = "Exchange name", sessionToken = NextToken, expiresAt = Now.AddHours(8),
        refreshToken = NextRefresh, refreshExpiresAt = Now.AddDays(30)
    });
    private static Task<HttpResponseMessage> Me(bool banned = false) => Json(new
    { accountId = AccountId, displayName = "Verified name", expiresAt = Now.AddHours(8), isBanned = banned });
    private static Task<HttpResponseMessage> Json(object value) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
    { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") });
    private static HttpClient Client(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) => new(new Handler(respond));
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); return respond(request); }
    }
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
}
