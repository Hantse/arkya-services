using System.Net;
using Arkya.Admin.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Arkya.Admin.Tests;

public sealed class AdminSessionTests
{
    [Fact]
    public async Task ExpiredAccessIsRenewedWithRotatingHashedRefreshCookieAndAbsoluteExpiry()
    {
        await using var factory = new AdminFactory { EnvironmentName = "Production" };
        using var browser = factory.Browser();
        using var login = await LoginAsync(browser);
        var original = Cookie(login, AdminSessions.RefreshCookie);
        var raw = login.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith(AdminSessions.RefreshCookie + "="));
        Assert.Contains("secure", raw);
        Assert.Contains("httponly", raw);
        Assert.Contains("samesite=strict", raw);
        Assert.Contains("expires=", raw);
        var session = Assert.Single(factory.Sessions.All);
        Assert.DoesNotContain(original.Split('.')[1], session.RefreshTokenHash);
        var originalExpiry = session.ExpiresAt;
        factory.Clock.Advance(TimeSpan.FromHours(1));
        using var renewed = await browser.GetAsync("/Quests");
        Assert.Equal(HttpStatusCode.OK, renewed.StatusCode);
        Assert.NotEqual(original, Cookie(renewed, AdminSessions.RefreshCookie));
        var current = Assert.Single(factory.Sessions.All);
        Assert.Equal(originalExpiry, current.ExpiresAt);
        Assert.Single(current.UsedTokenHashes);
        var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get("Cookies");
        var ticket = options.TicketDataFormat.Unprotect(Cookie(renewed, "Arkya.Admin"))!;
        Assert.Equal((factory.Clock.GetUtcNow() + TimeSpan.FromMinutes(30)).ToUnixTimeSeconds(), ticket.Properties.ExpiresUtc!.Value.ToUnixTimeSeconds());
        factory.Clock.Advance(TimeSpan.FromDays(30));
        Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync("/Quests")).StatusCode);
        Assert.Empty(factory.Sessions.All);
    }

    [Fact]
    public async Task ConcurrentTabsDoNotRotateTwiceButConsumedCookieReplayRevokesTheSession()
    {
        await using var factory = new AdminFactory();
        using var browser = factory.Browser();
        using var login = await LoginAsync(browser);
        var original = Cookie(login, AdminSessions.RefreshCookie);
        factory.Clock.Advance(TimeSpan.FromHours(1));
        using var renewed = await browser.GetAsync("/Quests");
        Assert.Equal(HttpStatusCode.OK, renewed.StatusCode);
        using var parallel = factory.Browser();
        parallel.DefaultRequestHeaders.Add("Cookie", AdminSessions.RefreshCookie + "=" + original);
        using var tolerated = await parallel.GetAsync("/Quests");
        Assert.Equal(HttpStatusCode.OK, tolerated.StatusCode);
        Assert.DoesNotContain(tolerated.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [],
            value => value.StartsWith("Arkya.Admin=" ) || value.StartsWith(AdminSessions.RefreshCookie + "="));
        factory.Clock.Advance(TimeSpan.FromSeconds(11));
        Assert.Equal(HttpStatusCode.Redirect, (await parallel.GetAsync("/Quests")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync("/Quests")).StatusCode);
        Assert.Empty(factory.Sessions.All);
    }

    [Fact]
    public async Task LogoutRevokesCopiedAccessAndRefreshCredentialsAndRequiresAntiforgery()
    {
        await using var factory = new AdminFactory();
        using var browser = factory.Browser();
        using var login = await LoginAsync(browser);
        var copied = "Arkya.Admin=" + Cookie(login, "Arkya.Admin") + "; " + AdminSessions.RefreshCookie + "=" + Cookie(login, AdminSessions.RefreshCookie);
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostAsync("/Logout", AdminFactory.Form(new()))).StatusCode);
        Assert.Single(factory.Sessions.All);
        var token = await AdminFactory.Token(browser);
        using var logout = await browser.PostAsync("/Logout", AdminFactory.Form(new() { ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.Empty(factory.Sessions.All);
        using var replay = factory.Browser();
        replay.DefaultRequestHeaders.Add("Cookie", copied);
        Assert.Equal(HttpStatusCode.Redirect, (await replay.GetAsync("/Quests")).StatusCode);
    }

    [Fact]
    public async Task PasswordRotationRevokesExistingAccessAndRefresh()
    {
        await using var factory = new AdminFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var configuration = factory.Services.GetRequiredService<IConfiguration>();
        configuration["Admin:Password"] = "replacement-admin-password";
        ((IConfigurationRoot)configuration).Reload();
        Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync("/Quests")).StatusCode);
        Assert.Empty(factory.Sessions.All);
    }

    [Fact]
    public async Task StoreOutageCannotBypassAuthenticationOrExposeDependencyDetails()
    {
        await using var factory = new AdminFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        factory.Sessions.Unavailable = true;
        using var response = await browser.GetAsync("/Quests");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Empty(factory.Content.Calls);
        AdminFactory.NoSecrets(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ReplacingTheApplicationCanRestoreSessionUsingPersistedRefreshOnly()
    {
        var path = Path.Combine(Path.GetTempPath(), "arkya-admin-refresh-test-" + Guid.NewGuid().ToString("N"));
        var store = new MemoryAdminSessionStore();
        try
        {
            string refresh;
            await using (var first = new AdminFactory { Sessions = store })
            {
                first.Settings["DataProtection:KeysPath"] = path;
                using var browser = first.Browser();
                using var login = await LoginAsync(browser);
                refresh = Cookie(login, AdminSessions.RefreshCookie);
            }
            await using var replacement = new AdminFactory { Sessions = store };
            replacement.Settings["DataProtection:KeysPath"] = path;
            replacement.Clock.Advance(TimeSpan.FromHours(2));
            using var restored = replacement.Browser();
            restored.DefaultRequestHeaders.Add("Cookie", AdminSessions.RefreshCookie + "=" + refresh);
            using var response = await restored.GetAsync("/Quests");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotEqual(refresh, Cookie(response, AdminSessions.RefreshCookie));
        }
        finally
        {
            if (Directory.Exists(path))
            {
                foreach (var file in Directory.GetFiles(path, "key-*.xml")) File.Delete(file);
                Directory.Delete(path);
            }
        }
    }

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient browser)
    {
        var token = await AdminFactory.Token(browser, "/Login");
        var response = await browser.PostAsync("/Login", AdminFactory.Form(new()
        {
            ["Password"] = AdminFactory.Password, ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return response;
    }

    private static string Cookie(HttpResponseMessage response, string name) =>
        response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith(name + "="))
            .Split(';')[0][(name.Length + 1)..];
}
