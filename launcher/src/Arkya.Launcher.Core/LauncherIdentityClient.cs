using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Arkya.Launcher.Core;

public sealed class LauncherSession
{
    internal string SessionToken { get; }
    public string AccountId { get; }
    public string DisplayName { get; }
    public DateTimeOffset ExpiresAt { get; }

    public LauncherSession(string sessionToken, string accountId, string displayName, DateTimeOffset expiresAt)
    {
        if (string.IsNullOrWhiteSpace(sessionToken) || sessionToken.Length != 43 ||
            sessionToken.Any(value => !char.IsAsciiLetterOrDigit(value) && value is not '-' and not '_') ||
            !Guid.TryParseExact(accountId, "N", out _) ||
            string.IsNullOrWhiteSpace(displayName) || displayName.Length > 256 || displayName.Any(char.IsControl))
            throw new LauncherAuthenticationException("La réponse du service de connexion est invalide.", true);
        SessionToken = sessionToken;
        AccountId = accountId;
        DisplayName = displayName;
        ExpiresAt = expiresAt;
    }

    public bool IsUsableAt(DateTimeOffset now) => ExpiresAt > now.AddSeconds(15);
    public override string ToString() => nameof(LauncherSession);
}

public sealed class LauncherAuthenticationException(string message, bool sessionInvalid = false) : Exception(message)
{
    public bool SessionInvalid { get; } = sessionInvalid;
}

public sealed class LauncherIdentityClient
{
    public const string TrustedOrigin = "https://api.arkya.online";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient httpClient;
    private readonly TimeProvider clock;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;

    public LauncherIdentityClient(HttpClient httpClient, TimeProvider? clock = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        this.httpClient = httpClient;
        this.clock = clock ?? TimeProvider.System;
        this.delay = delay ?? ((duration, token) => Task.Delay(duration, token));
    }

    public static HttpClient CreateHttpClient() => new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.None
    }) { Timeout = Timeout.InfiniteTimeSpan };

    public async Task<LauncherSession> LoginWithDiscordAsync(Action<Uri> openBrowser, CancellationToken cancellationToken)
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var proof = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lifetime.CancelAfter(TimeSpan.FromMinutes(5));
        var started = clock.GetUtcNow();
        ChallengeResponse challenge;
        using (var request = Request(HttpMethod.Post, "/v1/auth/discord/challenges", new { codeChallenge = proof }))
        using (var response = await SendAsync(request, lifetime.Token))
        {
            RequireSuccess(response);
            challenge = await ReadAsync<ChallengeResponse>(response, lifetime.Token);
        }
        var expiresAt = challenge.ExpiresAt < started.AddMinutes(5) ? challenge.ExpiresAt : started.AddMinutes(5);
        if (expiresAt <= clock.GetUtcNow()) throw ChallengeExpired();
        var browserUri = ValidateAuthorizationUrl(challenge.AuthorizationUrl, challenge.ChallengeId);
        cancellationToken.ThrowIfCancellationRequested();
        openBrowser(browserUri);
        while (clock.GetUtcNow() < expiresAt)
        {
            lifetime.Token.ThrowIfCancellationRequested();
            using var request = Request(HttpMethod.Post, "/v1/auth/discord/exchange",
                new { challengeId = challenge.ChallengeId, codeVerifier = verifier, useRefreshCookie = false });
            using var response = await SendAsync(request, lifetime.Token);
            if (response.StatusCode == HttpStatusCode.Accepted)
            {
                var retry = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(5);
                retry = TimeSpan.FromSeconds(Math.Clamp(retry.TotalSeconds, 5, 15));
                var remaining = expiresAt - clock.GetUtcNow();
                if (remaining <= TimeSpan.Zero) break;
                await delay(retry < remaining ? retry : remaining, lifetime.Token);
                continue;
            }
            if (response.StatusCode == HttpStatusCode.Gone) throw ChallengeExpired();
            RequireSuccess(response);
            var received = await ReadAsync<ExchangeResponse>(response, lifetime.Token);
            var session = new LauncherSession(received.SessionToken, received.AccountId, received.DisplayName, received.ExpiresAt);
            var verified = await VerifySessionAsync(session, lifetime.Token);
            lifetime.Token.ThrowIfCancellationRequested();
            return verified;
        }
        throw ChallengeExpired();
    }

    public async Task<LauncherSession> VerifySessionAsync(LauncherSession session, CancellationToken cancellationToken)
    {
        RequireUsable(session);
        using var request = Request(HttpMethod.Get, "/v1/auth/me", session: session);
        using var response = await SendAsync(request, cancellationToken);
        RequireSuccess(response);
        var account = await ReadAsync<AccountResponse>(response, cancellationToken);
        if (account.IsBanned || !string.Equals(account.AccountId, session.AccountId, StringComparison.Ordinal))
            throw new LauncherAuthenticationException("La session n’est plus valide. Reconnectez-vous avec Discord.", true);
        var expiresAt = account.ExpiresAt < session.ExpiresAt ? account.ExpiresAt : session.ExpiresAt;
        var verified = new LauncherSession(session.SessionToken, account.AccountId, account.DisplayName, expiresAt);
        RequireUsable(verified);
        cancellationToken.ThrowIfCancellationRequested();
        return verified;
    }

    public async Task LogoutAsync(LauncherSession session, CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Post, "/v1/auth/logout", session: session);
        using var response = await SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized) RequireSuccess(response);
    }

    public static Uri ValidateAuthorizationUrl(string address, string challengeId)
    {
        if (challengeId is null || challengeId.Length != 32 || challengeId.Any(value => !char.IsAsciiHexDigit(value)) ||
            !Uri.TryCreate(address, UriKind.Absolute, out var uri) || !IsTrustedOrigin(uri) ||
            uri.AbsolutePath != "/v1/auth/discord/authorize/" + challengeId || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new LauncherAuthenticationException("L’adresse de connexion reçue est invalide.");
        return uri;
    }

    private static bool IsTrustedOrigin(Uri uri) => uri.Scheme == Uri.UriSchemeHttps &&
        uri.IdnHost.Equals("api.arkya.online", StringComparison.OrdinalIgnoreCase) && uri.Port == 443 &&
        uri.UserInfo.Length == 0;

    private static HttpRequestMessage Request(HttpMethod method, string path, object? body = null, LauncherSession? session = null)
    {
        var request = new HttpRequestMessage(method, TrustedOrigin + path);
        if (body is not null) request.Content = JsonContent.Create(body, options: JsonOptions);
        if (session is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.SessionToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if ((int)response.StatusCode is >= 300 and < 400 ||
            response.RequestMessage?.RequestUri is { } actual && (!IsTrustedOrigin(actual) || actual != request.RequestUri))
        {
            response.Dispose();
            throw new LauncherAuthenticationException("Le service de connexion a retourné une redirection inattendue.");
        }
        return response;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        if (response.Content.Headers.ContentLength > 16384) throw InvalidResponse();
        await using var source = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var destination = new MemoryStream();
        try
        {
            await ReleaseClient.CopyBoundedAsync(source, destination, 16384, timeout.Token);
            return JsonSerializer.Deserialize<T>(destination.ToArray(), JsonOptions) ?? throw InvalidResponse();
        }
        catch (JsonException) { throw InvalidResponse(); }
        catch (InvalidDataException) { throw InvalidResponse(); }
    }

    private void RequireUsable(LauncherSession session)
    {
        if (!session.IsUsableAt(clock.GetUtcNow()))
            throw new LauncherAuthenticationException("Votre session a expiré. Reconnectez-vous avec Discord.", true);
    }

    private static void RequireSuccess(HttpResponseMessage response)
    {
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new LauncherAuthenticationException("La connexion a été refusée ou la session n’est plus valide.", true);
        if (!response.IsSuccessStatusCode)
            throw new LauncherAuthenticationException("Le service de connexion est indisponible. Réessayez dans un instant.");
    }

    private static LauncherAuthenticationException InvalidResponse() => new("La réponse du service de connexion est invalide.");
    private static LauncherAuthenticationException ChallengeExpired() => new("La demande de connexion a expiré. Recommencez la connexion Discord.");
    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private sealed record ChallengeResponse(string ChallengeId, string AuthorizationUrl, DateTimeOffset ExpiresAt);
    private sealed record ExchangeResponse(string AccountId, string DisplayName, string SessionToken, DateTimeOffset ExpiresAt);
    private sealed record AccountResponse(string AccountId, string DisplayName, DateTimeOffset ExpiresAt, bool IsBanned);
}
