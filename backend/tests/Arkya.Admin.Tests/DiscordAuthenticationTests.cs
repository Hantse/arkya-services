using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Arkya.Admin.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Arkya.Admin.Tests;

internal sealed class DiscordStub : HttpMessageHandler
{
    public const string AllowedId = "123456789012345678";
    public const string OtherId = "223456789012345678";
    public const string ClientId = "323456789012345678";
    public const string ClientSecret = "discord-test-client-secret";
    public const string AccessToken = "discord-test-access-token";
    public string UserId { get; set; } = AllowedId;
    public HttpStatusCode TokenStatus { get; set; } = HttpStatusCode.OK;
    public string? UserBody { get; set; }
    public int TokenCalls { get; private set; }
    public int UserCalls { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri!.AbsoluteUri == "https://discord.com/api/oauth2/token")
        {
            TokenCalls++;
            Assert.Equal(HttpMethod.Post, request.Method);
            var body = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(cancellationToken));
            Assert.Equal(ClientId, body["client_id"].ToString());
            Assert.Equal(ClientSecret, body["client_secret"].ToString());
            Assert.Equal("https://localhost/signin-discord", body["redirect_uri"].ToString());
            Assert.Equal("authorization_code", body["grant_type"].ToString());
            return new(TokenStatus)
            {
                Content = JsonContent.Create(new
                {
                    access_token = AccessToken, token_type = "Bearer", expires_in = 3600,
                    refresh_token = "never-store-this-refresh-token", scope = "identify",
                    error_description = "provider-private-error-detail"
                })
            };
        }
        Assert.Equal("https://discord.com/api/v10/users/@me", request.RequestUri.AbsoluteUri);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal(AccessToken, request.Headers.Authorization.Parameter);
        UserCalls++;
        return new(HttpStatusCode.OK)
        {
            Content = UserBody is null ? JsonContent.Create(new { id = UserId, username = "hatya_leash" }) : new StringContent(UserBody)
        };
    }
}

public sealed class DiscordAuthenticationTests
{
    private static AdminFactory Factory()
    {
        var factory = new AdminFactory { EnvironmentName = "Production" };
        factory.Settings["Admin:DiscordEnabled"] = "true";
        factory.Settings["Admin:Password"] = "";
        factory.Settings["Admin:AllowedDiscordUserIds:0"] = DiscordStub.AllowedId;
        factory.Settings["Discord:ClientId"] = DiscordStub.ClientId;
        factory.Settings["Discord:ClientSecret"] = DiscordStub.ClientSecret;
        factory.Settings["Discord:PublicBaseUrl"] = "https://localhost";
        return factory;
    }

    [Fact]
    public async Task DiscordLoginUsesAntiforgeryFixedCallbackAndOnlyIdentifyScope()
    {
        await using var factory = Factory();
        factory.Settings["AllowedHosts"] = "localhost;other.example";
        using var browser = factory.Browser();
        var html = WebUtility.HtmlDecode(await browser.GetStringAsync("/Login"));
        Assert.Contains("Continuer avec Discord", html);
        Assert.DoesNotContain("Mot de passe administrateur", html);
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostAsync("/Login?handler=Discord", AdminFactory.Form(new()))).StatusCode);
        browser.DefaultRequestHeaders.Host = "other.example";
        using var challenge = await ChallengeAsync(browser);
        var uri = challenge.Headers.Location!;
        Assert.Equal("discord.com", uri.Host);
        var query = QueryHelpers.ParseQuery(uri.Query);
        Assert.Equal("https://localhost/signin-discord", query["redirect_uri"].ToString());
        Assert.Equal("identify", query["scope"].ToString());
        Assert.Equal("code", query["response_type"].ToString());
        Assert.False(string.IsNullOrEmpty(query["state"]));
        Assert.DoesNotContain(DiscordStub.ClientSecret, uri.AbsoluteUri);
        var cookie = challenge.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith(".AspNetCore.Correlation."));
        Assert.Contains("secure", cookie);
        Assert.Contains("httponly", cookie);
        Assert.Contains("samesite=none", cookie);
        Assert.Contains("form-action 'self' https://discord.com", challenge.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task OnlyAllowedVerifiedIdGetsAdministratorCookieWithoutProviderTokens()
    {
        await using var factory = Factory();
        using var browser = factory.Browser();
        using var challenge = await ChallengeAsync(browser, "/Realms");
        using var callback = await browser.GetAsync(Callback(challenge));
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal("/Realms", callback.Headers.Location!.OriginalString);
        var cookie = callback.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("Arkya.Admin="));
        Assert.Contains("secure", cookie);
        Assert.Contains("httponly", cookie);
        Assert.Contains("samesite=lax", cookie);
        var value = cookie.Split(';')[0]["Arkya.Admin=".Length..];
        var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var ticket = options.TicketDataFormat.Unprotect(value)!;
        Assert.True(ticket.Principal.IsInRole("Administrator"));
        Assert.Equal(DiscordStub.AllowedId, ticket.Principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal("Discord", ticket.Principal.FindFirstValue(AdminDiscordAuthentication.ProviderClaim));
        Assert.Empty(ticket.Properties.GetTokens());
        Assert.DoesNotContain(DiscordStub.AccessToken, JsonSerializer.Serialize(ticket.Properties.Items));
        Assert.Equal(1, factory.Discord.TokenCalls);
        Assert.Equal(1, factory.Discord.UserCalls);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/Realms")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostAsync("/Logout", AdminFactory.Form(new()))).StatusCode);
        var token = await AdminFactory.Token(browser, "/Realms");
        Assert.Equal(HttpStatusCode.Redirect, (await browser.PostAsync("/Logout",
            AdminFactory.Form(new() { ["__RequestVerificationToken"] = token }))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync("/Realms")).StatusCode);
    }

    [Fact]
    public async Task IdenticalUsernameCannotAuthorizeAnotherDiscordId()
    {
        await using var factory = Factory();
        factory.Discord.UserId = DiscordStub.OtherId;
        using var browser = factory.Browser();
        using var challenge = await ChallengeAsync(browser);
        using var callback = await browser.GetAsync(Callback(challenge));
        AssertDenied(callback);
        Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync("/")).StatusCode);
    }

    [Theory]
    [InlineData("{\"id\":123456789012345678,\"username\":\"hatya_leash\"}")]
    [InlineData("{\"username\":\"hatya_leash\"}")]
    [InlineData("{\"id\":\"invalid\",\"username\":\"hatya_leash\"}")]
    [InlineData("not-json")]
    public async Task MalformedProviderIdentityFailsClosed(string body)
    {
        await using var factory = Factory();
        factory.Discord.UserBody = body;
        using var browser = factory.Browser();
        using var challenge = await ChallengeAsync(browser);
        using var callback = await browser.GetAsync(Callback(challenge));
        AssertDenied(callback);
    }

    [Fact]
    public async Task ProviderDenialAndTokenFailureReturnOnlySafeLocalErrors()
    {
        await using var factory = Factory();
        using var browser = factory.Browser();
        using (var challenge = await ChallengeAsync(browser))
        {
            using var denied = await browser.GetAsync(Callback(challenge, "error=access_denied&error_description=provider-private-error-detail"));
            AssertDenied(denied);
            Assert.Equal(0, factory.Discord.TokenCalls);
        }
        factory.Discord.TokenStatus = HttpStatusCode.BadRequest;
        using var retry = await ChallengeAsync(browser);
        using var failed = await browser.GetAsync(Callback(retry));
        AssertDenied(failed);
        var error = await browser.GetStringAsync("/Login?error=discord");
        Assert.DoesNotContain("provider-private-error-detail", error);
        Assert.DoesNotContain(DiscordStub.AccessToken, error);
        Assert.DoesNotContain(DiscordStub.ClientSecret, error);
    }

    [Fact]
    public async Task MissingCorrelationTamperedStateAndReplayedCallbackNeverAuthenticate()
    {
        await using var factory = Factory();
        using var browser = factory.Browser();
        using var challenge = await ChallengeAsync(browser);
        using var stranger = factory.Browser();
        using var missing = await stranger.GetAsync(Callback(challenge));
        AssertDenied(missing);
        using var tampered = await browser.GetAsync("/signin-discord?code=test-code&state=forged");
        AssertDenied(tampered);
        Assert.Equal(0, factory.Discord.TokenCalls);
        using var valid = await browser.GetAsync(Callback(challenge));
        Assert.Equal("/", valid.Headers.Location!.OriginalString);
        using var replay = await browser.GetAsync(Callback(challenge));
        AssertDenied(replay);
        Assert.Equal(1, factory.Discord.TokenCalls);
    }

    [Fact]
    public async Task WrongIssuerCannotExchangeAnAuthorizationCode()
    {
        await using var factory = Factory();
        using var browser = factory.Browser();
        using var challenge = await ChallengeAsync(browser);
        using var callback = await browser.GetAsync(Callback(challenge, "code=test-code&iss=https%3A%2F%2Fattacker.example"));
        AssertDenied(callback);
        Assert.Equal(0, factory.Discord.TokenCalls);
    }

    [Theory]
    [InlineData("https://attacker.example")]
    [InlineData("//attacker.example")]
    [InlineData("/\\attacker.example")]
    [InlineData("/Realms\r\nLocation: https://attacker.example")]
    public async Task ReturnUrlCannotLeaveTheAdminOrigin(string returnUrl)
    {
        await using var factory = Factory();
        using var browser = factory.Browser();
        using var challenge = await ChallengeAsync(browser, returnUrl);
        using var callback = await browser.GetAsync(Callback(challenge));
        Assert.Equal("/", callback.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task PasswordLoginAndLegacyPasswordCookiesCannotAuthorizeDiscordMode()
    {
        await using var factory = Factory();
        factory.Settings["Admin:Password"] = AdminFactory.Password;
        using var browser = factory.Browser();
        var token = await AdminFactory.Token(browser, "/Login");
        using var passwordLogin = await browser.PostAsync("/Login", AdminFactory.Form(new()
        {
            ["Password"] = AdminFactory.Password, ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.NotFound, passwordLogin.StatusCode);
        var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "Administrateur"),
            new Claim(ClaimTypes.Role, "Administrator")], CookieAuthenticationDefaults.AuthenticationScheme));
        var value = options.TicketDataFormat.Protect(new AuthenticationTicket(principal, new AuthenticationProperties
        {
            IssuedUtc = DateTimeOffset.UtcNow, ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1)
        }, CookieAuthenticationDefaults.AuthenticationScheme));
        using var legacy = factory.Browser();
        legacy.DefaultRequestHeaders.Add("Cookie", "Arkya.Admin=" + value);
        Assert.Equal(HttpStatusCode.Redirect, (await legacy.GetAsync("/Realms")).StatusCode);
    }

    [Fact]
    public async Task RemovingIdFromAllowlistRevokesAnExistingCookieOnTheNextRequest()
    {
        await using var factory = Factory();
        using var browser = factory.Browser();
        using var challenge = await ChallengeAsync(browser);
        using var callback = await browser.GetAsync(Callback(challenge));
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/Realms")).StatusCode);
        var configuration = factory.Services.GetRequiredService<IConfiguration>();
        configuration["Admin:AllowedDiscordUserIds:0"] = DiscordStub.OtherId;
        ((IConfigurationRoot)configuration).Reload();
        Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync("/Realms")).StatusCode);
    }

    [Theory]
    [InlineData("Admin:AllowedDiscordUserIds:0", "")]
    [InlineData("Admin:AllowedDiscordUserIds:0", "hatya_leash")]
    [InlineData("Admin:AllowedDiscordUserIds:0", "18446744073709551616")]
    [InlineData("Discord:ClientSecret", "")]
    [InlineData("Discord:ClientId", "not-an-id")]
    [InlineData("Discord:PublicBaseUrl", "http://admin.example")]
    [InlineData("Discord:PublicBaseUrl", "https://admin.example/path")]
    [InlineData("Discord:PublicBaseUrl", "https://user@admin.example")]
    [InlineData("Discord:PublicBaseUrl", "https://admin.example?query=1")]
    public void InvalidDiscordConfigurationFailsStartup(string key, string value)
    {
        using var factory = Factory();
        factory.Settings[key] = value;
        var exception = Record.Exception(() => factory.Browser());
        if (exception is AggregateException aggregate)
            Assert.All(aggregate.Flatten().InnerExceptions, inner => Assert.IsType<OptionsValidationException>(inner));
        else
            Assert.IsType<OptionsValidationException>(exception);
    }

    private static async Task<HttpResponseMessage> ChallengeAsync(HttpClient browser, string returnUrl = "/")
    {
        var token = await AdminFactory.Token(browser, "/Login");
        var response = await browser.PostAsync("/Login?handler=Discord", AdminFactory.Form(new()
        {
            ["__RequestVerificationToken"] = token, ["ReturnUrl"] = returnUrl
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return response;
    }

    private static string Callback(HttpResponseMessage challenge, string parameters = "code=test-code")
    {
        var state = QueryHelpers.ParseQuery(challenge.Headers.Location!.Query)["state"].ToString();
        return "/signin-discord?" + parameters + "&state=" + Uri.EscapeDataString(state);
    }

    private static void AssertDenied(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Login?error=discord", response.Headers.Location!.OriginalString);
        Assert.DoesNotContain(response.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [],
            cookie => cookie.StartsWith("Arkya.Admin="));
    }
}
