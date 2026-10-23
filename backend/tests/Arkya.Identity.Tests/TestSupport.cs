using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Arkya.Identity.Api.Application;
using Arkya.Identity.Api.Domain;
using Arkya.Identity.Api.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Arkya.Identity.Tests;

internal sealed class MutableTimeProvider : TimeProvider
{
    private DateTimeOffset now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
    private readonly object gate = new();
    public override DateTimeOffset GetUtcNow() { lock (gate) return now; }
    public void Advance(TimeSpan elapsed) { lock (gate) now += elapsed; }
}

internal sealed class FakeDiscordIdentityClient : IDiscordIdentityClient
{
    private int calls;
    public int Calls => Volatile.Read(ref calls);
    public DiscordUser User { get; set; } = new("123456789012345678", "Discord Player");
    public Exception? Failure { get; set; }

    public Task<DiscordUser> ExchangeCodeAsync(string code, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref calls);
        Assert.Equal("discord-authorization-code", code);
        return Failure is { } failure ? Task.FromException<DiscordUser>(failure) : Task.FromResult(User);
    }
}

// Atomic conditions match the storage port without depending on Mongo TTL cleanup.
internal sealed class MemoryIdentityStore : IIdentityStore
{
    private readonly object gate = new();
    private readonly Dictionary<string, LoginChallenge> challenges = [];
    private readonly Dictionary<string, Account> accounts = [];
    private readonly Dictionary<string, Session> sessions = [];
    private readonly Dictionary<string, RefreshSession> refreshSessions = [];

    public Session[] Sessions { get { lock (gate) return sessions.Values.ToArray(); } }
    public Account[] Accounts { get { lock (gate) return accounts.Values.ToArray(); } }
    public RefreshSession[] RefreshSessions { get { lock (gate) return refreshSessions.Values.ToArray(); } }

    public Task CreateChallengeAsync(LoginChallenge challenge, CancellationToken cancellationToken)
    {
        lock (gate) challenges.Add(challenge.Id, challenge);
        return Task.CompletedTask;
    }

    public Task<LoginChallenge?> GetChallengeAsync(string id, CancellationToken cancellationToken)
    {
        lock (gate) return Task.FromResult(challenges.GetValueOrDefault(id));
    }

    public Task<bool> StartBrowserAsync(string id, string stateHash, string browserNonceHash, DateTime now, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (!challenges.TryGetValue(id, out var challenge) || challenge.Status != ChallengeStatus.Pending || challenge.StateHash is not null || challenge.ExpiresAt <= now)
                return Task.FromResult(false);
            challenges[id] = challenge with { StateHash = stateHash, BrowserNonceHash = browserNonceHash };
            return Task.FromResult(true);
        }
    }

    public Task<LoginChallenge?> FindStateAsync(string stateHash, CancellationToken cancellationToken)
    {
        lock (gate) return Task.FromResult(challenges.Values.SingleOrDefault(challenge => challenge.StateHash == stateHash));
    }

    public Task<LoginChallenge?> ClaimStateAsync(string stateHash, string browserNonceHash, DateTime now, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            var challenge = challenges.Values.SingleOrDefault(item => item.StateHash == stateHash && item.BrowserNonceHash == browserNonceHash && item.Status == ChallengeStatus.Pending && item.ExpiresAt > now);
            if (challenge is null) return Task.FromResult<LoginChallenge?>(null);
            challenges[challenge.Id] = challenge with { Status = ChallengeStatus.Processing };
            return Task.FromResult<LoginChallenge?>(challenge);
        }
    }

    public Task CompleteChallengeAsync(string id, Account? account, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (challenges.TryGetValue(id, out var challenge) && challenge.Status == ChallengeStatus.Processing)
                challenges[id] = challenge with { Status = account is null ? ChallengeStatus.Failed : ChallengeStatus.Completed, AccountId = account?.Id,
                    DisplayName = account?.DisplayName, AccountSessionVersion = account?.SessionVersion ?? 0 };
        }
        return Task.CompletedTask;
    }

    public Task<LoginChallenge?> RedeemChallengeAsync(string id, string codeChallenge, DateTime now, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (!challenges.TryGetValue(id, out var challenge) || challenge.CodeChallenge != codeChallenge || challenge.Status != ChallengeStatus.Completed || challenge.ExpiresAt <= now)
                return Task.FromResult<LoginChallenge?>(null);
            challenges[id] = challenge with { Status = ChallengeStatus.Consumed };
            return Task.FromResult<LoginChallenge?>(challenge);
        }
    }

    public Task<Account> UpsertAccountAsync(string discordId, string displayName, DateTime now, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            var account = accounts.TryGetValue(discordId, out var existing)
                ? existing with { DisplayName = displayName }
                : new(Guid.NewGuid().ToString(), discordId, displayName, CreatedAt: now, UpdatedAt: now);
            accounts[discordId] = account;
            return Task.FromResult(account);
        }
    }

    public Task<Account?> GetAccountAsync(string accountId, CancellationToken cancellationToken)
    {
        lock (gate) return Task.FromResult(accounts.Values.SingleOrDefault(account => account.Id == accountId));
    }

    public Task<(IReadOnlyList<Account> Items, long Total)> ListAccountsAsync(string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            var filtered = accounts.Values.Where(account => string.IsNullOrEmpty(search) || account.Id == search || account.DiscordId == search ||
                account.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)).OrderBy(account => account.DisplayName).ThenBy(account => account.Id).ToArray();
            return Task.FromResult<(IReadOnlyList<Account>, long)>((filtered.Skip((page - 1) * pageSize).Take(pageSize).ToArray(), filtered.LongLength));
        }
    }

    public Task<Account?> AdministerAccountAsync(string accountId, long expectedRevision, string action, bool? hasBetaAccess,
        string reason, string actor, DateTime now, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            var account = accounts.Values.SingleOrDefault(value => value.Id == accountId);
            if (account is null || account.Revision != expectedRevision) return Task.FromResult<Account?>(null);
            var updated = account with { Revision = account.Revision + 1, UpdatedAt = now };
            updated = action switch
            {
                "ban" => updated with { IsBanned = true, BanReason = reason, SessionVersion = account.SessionVersion + 1 },
                "unban" => updated with { IsBanned = false, BanReason = null },
                "beta-access" => updated with { HasBetaAccess = hasBetaAccess!.Value },
                _ => throw new ArgumentException(action)
            };
            accounts[account.DiscordId] = updated;
            if (action == "ban")
                foreach (var hash in sessions.Values.Where(value => value.AccountId == accountId && value.SessionVersion < updated.SessionVersion).Select(value => value.TokenHash).ToArray())
                    sessions.Remove(hash);
            return Task.FromResult<Account?>(updated);
        }
    }

    public Task CreateSessionAsync(Session session, CancellationToken cancellationToken)
    {
        lock (gate) sessions.Add(session.TokenHash, session);
        return Task.CompletedTask;
    }

    public Task<Session?> GetSessionAsync(string tokenHash, DateTime now, CancellationToken cancellationToken)
    {
        lock (gate) return Task.FromResult(sessions.TryGetValue(tokenHash, out var session) && session.ExpiresAt > now ? session : null);
    }

    public Task RevokeSessionAsync(string tokenHash, CancellationToken cancellationToken)
    {
        lock (gate) sessions.Remove(tokenHash);
        return Task.CompletedTask;
    }

    public Task CreateRefreshSessionAsync(RefreshSession session, CancellationToken cancellationToken)
    {
        lock (gate) refreshSessions.Add(session.Id, session);
        return Task.CompletedTask;
    }

    public Task<RefreshSession?> GetRefreshSessionAsync(string id, CancellationToken cancellationToken)
    {
        lock (gate) return Task.FromResult(refreshSessions.GetValueOrDefault(id));
    }

    public Task<RefreshSession?> RotateRefreshSessionAsync(string id, string tokenHash, string replacementHash, DateTime now,
        CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (!refreshSessions.TryGetValue(id, out var session) || session.Revoked || session.ExpiresAt <= now ||
                session.TokenHash != tokenHash || session.UsedTokenHashes.Length >= RefreshSession.MaximumRotations)
                return Task.FromResult<RefreshSession?>(null);
            var rotated = session with { TokenHash = replacementHash, UsedTokenHashes = [.. session.UsedTokenHashes, tokenHash] };
            refreshSessions[id] = rotated;
            return Task.FromResult<RefreshSession?>(rotated);
        }
    }

    public Task RevokeRefreshSessionAsync(string id, CancellationToken cancellationToken)
    {
        lock (gate)
            if (refreshSessions.TryGetValue(id, out var session)) refreshSessions[id] = session with { Revoked = true };
        return Task.CompletedTask;
    }
}

internal sealed class IdentityFactory : WebApplicationFactory<global::Program>
{
    public const string ServiceKey = "identity-inbound-test-service-key-1234";
    public string[] WebClientOrigins { get; init; } = [];
    public string EnvironmentName { get; init; } = "Development";
    public IReadOnlyDictionary<string, string?> AdditionalConfiguration { get; init; } = new Dictionary<string, string?>();
    public MemoryIdentityStore Store { get; } = new();
    public MutableTimeProvider Clock { get; } = new();
    public FakeDiscordIdentityClient Discord { get; } = new();
    public FakeDiscordSocialClient Social { get; } = new();

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var observerReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        builder.ConfigureServices(services => services.AddSingleton<IHostLifetime>(new StartupGateLifetime(observerReady.Task)));
        var host = builder.Build();
        try
        {
            // Register the factory's startup observer before validation can fail and app.Run disposes the host.
            var startup = host.StartAsync();
            observerReady.SetResult();
            startup.GetAwaiter().GetResult();
            return host;
        }
        catch
        {
            host.Dispose();
            throw;
        }
    }

    private sealed class StartupGateLifetime(Task observerReady) : IHostLifetime
    {
        public Task WaitForStartAsync(CancellationToken cancellationToken) => observerReady.WaitAsync(cancellationToken);
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var values = new Dictionary<string, string?>
            {
                ["ServiceAuthentication:Key"] = ServiceKey,
                ["Discord:ClientId"] = "discord-test-client",
                ["Discord:ClientSecret"] = "discord-test-secret",
                ["Discord:RedirectUri"] = "https://localhost:7141/v1/auth/discord/callback",
                ["Discord:PublicBaseUrl"] = "https://localhost:7141",
                ["Mongo:ConnectionString"] = "mongodb://localhost:27017",
                ["Mongo:DatabaseName"] = "arkya_identity_tests",
                ["Sessions:Lifetime"] = "08:00:00"
            };
            for (var index = 0; index < WebClientOrigins.Length; index++)
                values[$"WebClient:AllowedOrigins:{index}"] = WebClientOrigins[index];
            foreach (var (key, value) in AdditionalConfiguration) values[key] = value;
            configuration.AddInMemoryCollection(values);
        });
        builder.ConfigureServices(services =>
        {
            var initializer = services.Single(descriptor => descriptor.ImplementationType == typeof(IdentityIndexInitializer));
            services.Remove(initializer);
            services.RemoveAll<IIdentityStore>();
            services.AddSingleton<IIdentityStore>(Store);
            services.RemoveAll<IDiscordIdentityClient>();
            services.AddSingleton<IDiscordIdentityClient>(Discord);
            services.RemoveAll<IDiscordSocialClient>();
            services.AddSingleton<IDiscordSocialClient>(Social);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    public HttpClient BrowserClient(bool cookies = true) => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost:7141"), AllowAutoRedirect = false, HandleCookies = cookies
    });
}

internal sealed record StartedLogin(ChallengeResult Challenge, string Verifier, string State, string Cookie, Uri DiscordUrl)
{
    public string CallbackUrl(string? error = null) => QueryHelpers.AddQueryString("/v1/auth/discord/callback", new Dictionary<string, string?>
    {
        ["state"] = State, ["code"] = error is null ? "discord-authorization-code" : null, ["error"] = error
    });
}

internal static class LoginFlow
{
    public static async Task<StartedLogin> StartAsync(HttpClient browser, string? returnUrl = null)
    {
        var verifier = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        using var response = await browser.PostAsJsonAsync("/v1/auth/discord/challenges", new { codeChallenge = IdentityService.CodeChallenge(verifier), returnUrl });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var challenge = (await response.Content.ReadFromJsonAsync<ChallengeResult>())!;
        using var authorization = await browser.GetAsync(new Uri(challenge.AuthorizationUrl).PathAndQuery);
        Assert.Equal(HttpStatusCode.Redirect, authorization.StatusCode);
        var url = authorization.Headers.Location!;
        var state = QueryHelpers.ParseQuery(url.Query)["state"].ToString();
        var cookie = Assert.Single(authorization.Headers.GetValues("Set-Cookie"));
        return new(challenge, verifier, state, cookie, url);
    }

    public static Task<HttpResponseMessage> ExchangeAsync(HttpClient client, StartedLogin login, string? verifier = null) =>
        client.PostAsJsonAsync("/v1/auth/discord/exchange", new { challengeId = login.Challenge.ChallengeId, codeVerifier = verifier ?? login.Verifier });

    public static async Task<SessionResult> CompleteAsync(HttpClient browser)
    {
        var login = await StartAsync(browser);
        using var callback = await browser.GetAsync(login.CallbackUrl());
        Assert.Equal(HttpStatusCode.OK, callback.StatusCode);
        using var exchange = await ExchangeAsync(browser, login);
        Assert.Equal(HttpStatusCode.OK, exchange.StatusCode);
        return (await exchange.Content.ReadFromJsonAsync<SessionResult>())!;
    }
}
