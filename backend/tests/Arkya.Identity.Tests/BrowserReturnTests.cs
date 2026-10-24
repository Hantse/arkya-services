using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arkya.Identity.Api.Application;
using Arkya.Identity.Api.Domain;
using Microsoft.AspNetCore.WebUtilities;

namespace Arkya.Identity.Tests;

public sealed class BrowserReturnTests
{
    private const string SiteOrigin = "https://arkya.online";
    private const string ReturnUrl = SiteOrigin + "/account.html?lang=fr&returnTo=play.html";

    [Theory]
    [InlineData(SiteOrigin + "/account.html")]
    [InlineData(SiteOrigin + "/account.html?lang=fr")]
    [InlineData(SiteOrigin + "/account.html?lang=en")]
    [InlineData(SiteOrigin + "/account.html?returnTo=play.html")]
    [InlineData(ReturnUrl)]
    [InlineData(SiteOrigin + "/account.html?returnTo=play.html&lang=en")]
    [InlineData(SiteOrigin + "/account.html?returnTo=beta.html&lang=fr")]
    public async Task WebLoginReturnsToPersistedAccountPageBeforeExchangingSession(string returnUrl)
    {
        await using var factory = new IdentityFactory { WebClientOrigins = [SiteOrigin] };
        using var browser = factory.BrowserClient();
        var login = await LoginFlow.StartAsync(browser, returnUrl);
        Assert.Equal(returnUrl, login.Challenge.ReturnUrl);
        Assert.Equal(returnUrl, (await factory.Store.GetChallengeAsync(login.Challenge.ChallengeId, CancellationToken.None))!.ReturnUrl);
        Assert.False(QueryHelpers.ParseQuery(login.DiscordUrl.Query).ContainsKey("returnUrl"));

        // An injected callback query must never override the target bound to the challenge.
        using var callback = await browser.GetAsync(login.CallbackUrl() + "&returnUrl=https%3A%2F%2Fattacker.example");
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal(returnUrl, callback.Headers.Location!.OriginalString);
        Assert.True(callback.Headers.CacheControl!.NoStore);
        Assert.Equal("no-referrer", Assert.Single(callback.Headers.GetValues("Referrer-Policy")));
        Assert.Empty(factory.Store.Sessions);
        using var exchange = await LoginFlow.ExchangeAsync(browser, login);
        Assert.Equal(HttpStatusCode.OK, exchange.StatusCode);
        Assert.Single(factory.Store.Sessions);

        using var replay = await browser.GetAsync(login.CallbackUrl());
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        Assert.Null(replay.Headers.Location);
        Assert.Equal(1, factory.Discord.Calls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/account.html")]
    [InlineData("//arkya.online/account.html")]
    [InlineData("https://attacker.example/account.html")]
    [InlineData("https://arkya.online.attacker.example/account.html")]
    [InlineData("https://arkya.online@attacker.example/account.html")]
    [InlineData("https://user:password@arkya.online/account.html")]
    [InlineData("http://arkya.online/account.html")]
    [InlineData("https://arkya.online:444/account.html")]
    [InlineData("https://api.arkya.online/account.html")]
    [InlineData("https://arkya.online/play.html")]
    [InlineData("https://arkya.online/account.html/")]
    [InlineData("https://arkya.online/a/../account.html")]
    [InlineData("https://arkya.online/%61ccount.html")]
    [InlineData("https://arkya.online\\account.html")]
    [InlineData("https://arkya.online/account.html#fragment")]
    [InlineData("https://arkya.online/account.html?")]
    [InlineData("https://arkya.online/account.html?lang=es")]
    [InlineData("https://arkya.online/account.html?lang=fr&lang=en")]
    [InlineData("https://arkya.online/account.html?lang=%66r")]
    [InlineData("https://arkya.online/account.html?lang=fr&")]
    [InlineData("https://arkya.online/account.html?next=https://attacker.example")]
    [InlineData("https://arkya.online/account.html?returnTo=https://attacker.example")]
    [InlineData("https://arkya.online/account.html?returnTo=//attacker.example")]
    [InlineData("https://arkya.online/account.html?returnTo=play.html&returnTo=play.html")]
    [InlineData("https://arkya.online/account.html?lang=fr%0d%0aLocation:https://attacker.example")]
    [InlineData("https://arkya.online/account.html\r\nLocation:https://attacker.example")]
    public async Task InvalidReturnTargetsAreRejectedBeforeCreatingChallenge(string returnUrl)
    {
        await using var factory = new IdentityFactory { WebClientOrigins = [SiteOrigin] };
        using var browser = factory.BrowserClient();
        using var response = await browser.PostAsJsonAsync("/v1/auth/discord/challenges",
            new { codeChallenge = IdentityService.CodeChallenge(new string('a', 43)), returnUrl });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_return_url", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Null(response.Headers.Location);
        Assert.Equal(0, factory.Discord.Calls);
    }

    [Fact]
    public async Task ReturnTargetRequiresConfiguredWebOrigin()
    {
        await using var factory = new IdentityFactory();
        using var browser = factory.BrowserClient();
        using var response = await browser.PostAsJsonAsync("/v1/auth/discord/challenges",
            new { codeChallenge = IdentityService.CodeChallenge(new string('a', 43)), returnUrl = ReturnUrl });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("access_denied")]
    [InlineData("provider_unavailable")]
    [InlineData("provider_bad_response")]
    [InlineData("provider_timeout")]
    public async Task VerifiedFailureReturnsToAccountPageWithFailedExchange(string failure)
    {
        await using var factory = new IdentityFactory { WebClientOrigins = [SiteOrigin] };
        factory.Discord.Failure = failure switch
        {
            "provider_unavailable" => new HttpRequestException("Provider unavailable"),
            "provider_bad_response" => new InvalidDataException("Invalid provider response"),
            "provider_timeout" => new TaskCanceledException("Provider timeout"),
            _ => null
        };
        using var browser = factory.BrowserClient();
        var login = await LoginFlow.StartAsync(browser, ReturnUrl);
        using var callback = await browser.GetAsync(login.CallbackUrl(failure == "access_denied" ? failure : null));
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal(ReturnUrl, callback.Headers.Location!.OriginalString);
        using var exchange = await LoginFlow.ExchangeAsync(browser, login);
        Assert.Equal(HttpStatusCode.Gone, exchange.StatusCode);
        Assert.Equal(ChallengeStatus.Failed, (await factory.Store.GetChallengeAsync(login.Challenge.ChallengeId, CancellationToken.None))!.Status);
        Assert.Empty(factory.Store.Accounts);
        Assert.Empty(factory.Store.Sessions);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("mismatched")]
    [InlineData("invalid")]
    public async Task InvalidBrowserCookieCannotRedirectOrConsumeChallenge(string cookie)
    {
        await using var factory = new IdentityFactory { WebClientOrigins = [SiteOrigin] };
        using var browser = factory.BrowserClient();
        using var stranger = factory.BrowserClient(cookies: false);
        var login = await LoginFlow.StartAsync(browser, ReturnUrl);
        if (cookie != "missing")
            stranger.DefaultRequestHeaders.Add("Cookie",
                $"{IdentityService.CookieName(login.Challenge.ChallengeId)}={(cookie == "mismatched" ? new string('x', 43) : "invalid")}");
        using var invalid = await stranger.GetAsync(login.CallbackUrl());
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Null(invalid.Headers.Location);
        Assert.Equal(0, factory.Discord.Calls);
        using var valid = await browser.GetAsync(login.CallbackUrl());
        Assert.Equal(HttpStatusCode.Redirect, valid.StatusCode);
        Assert.Equal(ReturnUrl, valid.Headers.Location!.OriginalString);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("invalid")]
    [InlineData("unknown")]
    [InlineData("expired")]
    public async Task InvalidStateCannotRedirect(string stateKind)
    {
        await using var factory = new IdentityFactory { WebClientOrigins = [SiteOrigin] };
        using var browser = factory.BrowserClient();
        var login = await LoginFlow.StartAsync(browser, ReturnUrl);
        if (stateKind == "expired") factory.Clock.Advance(TimeSpan.FromMinutes(5));
        var state = stateKind switch
        {
            "missing" => null,
            "invalid" => "invalid",
            "unknown" => new string('x', 43),
            _ => login.State
        };
        var callbackUrl = QueryHelpers.AddQueryString("/v1/auth/discord/callback", new Dictionary<string, string?>
        {
            ["state"] = state, ["code"] = "discord-authorization-code", ["returnUrl"] = ReturnUrl
        });
        using var callback = await browser.GetAsync(callbackUrl);
        Assert.Equal(HttpStatusCode.BadRequest, callback.StatusCode);
        Assert.Null(callback.Headers.Location);
        Assert.Equal(0, factory.Discord.Calls);
    }

    [Fact]
    public async Task NativeChallengeKeepsExistingResponseAndCallbackWithoutReturnUrl()
    {
        await using var factory = new IdentityFactory { WebClientOrigins = [SiteOrigin] };
        using var browser = factory.BrowserClient();
        using var response = await browser.PostAsJsonAsync("/v1/auth/discord/challenges",
            new { codeChallenge = IdentityService.CodeChallenge(new string('a', 43)) });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False((await response.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("returnUrl", out _));
        var login = await LoginFlow.StartAsync(browser);
        using var callback = await browser.GetAsync(login.CallbackUrl() + "&returnUrl=" + Uri.EscapeDataString(ReturnUrl));
        Assert.Equal(HttpStatusCode.OK, callback.StatusCode);
        Assert.Null(callback.Headers.Location);
        Assert.Contains("Vous pouvez retourner dans Arkya", await callback.Content.ReadAsStringAsync());
    }
}
