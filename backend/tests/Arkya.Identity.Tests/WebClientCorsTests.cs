using System.Net;
using System.Net.Http.Json;
using Arkya.Identity.Api.Application;
using Microsoft.Extensions.Options;

namespace Arkya.Identity.Tests;

public sealed class WebClientCorsTests
{
    private const string SiteOrigin = "https://arkya-atlas.easykrill33.chatgpt.site";

    [Theory]
    [InlineData("/v1/auth/discord/challenges", "POST")]
    [InlineData("/v1/auth/discord/exchange", "POST")]
    [InlineData("/v1/auth/me", "GET")]
    [InlineData("/v1/auth/logout", "POST")]
    public async Task ConfiguredSiteCanPreflightOnlyRequiredMethodsAndHeaders(string path, string method)
    {
        await using var factory = new IdentityFactory { WebClientOrigins = [SiteOrigin] };
        using var client = factory.BrowserClient();
        using var response = await PreflightAsync(client, path, SiteOrigin, method);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(SiteOrigin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Equal(["GET", "POST"], HeaderItems(response, "Access-Control-Allow-Methods"));
        Assert.Equal(["authorization", "content-type"], HeaderItems(response, "Access-Control-Allow-Headers").Select(value => value.ToLowerInvariant()).Order().ToArray());
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Theory]
    [InlineData("https://attacker.example")]
    [InlineData("https://arkya-atlas.easykrill33.chatgpt.site.attacker.example")]
    [InlineData("http://arkya-atlas.easykrill33.chatgpt.site")]
    [InlineData("null")]
    public async Task UnconfiguredOriginsReceiveNoCorsPermission(string origin)
    {
        await using var factory = new IdentityFactory { WebClientOrigins = [SiteOrigin] };
        using var client = factory.BrowserClient();
        using var response = await PreflightAsync(client, "/v1/auth/discord/challenges", origin, "POST");
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task EmptyDefaultDoesNotEnableBrowserAccess()
    {
        await using var factory = new IdentityFactory();
        using var client = factory.BrowserClient();
        using var response = await PreflightAsync(client, "/v1/auth/me", SiteOrigin, "GET");
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Theory]
    [InlineData("/internal/sessions/introspect", "POST")]
    [InlineData("/v1/auth/discord/social/config", "GET")]
    [InlineData("/v1/auth/discord/social/exchange", "POST")]
    [InlineData("/v1/auth/discord/social/refresh", "POST")]
    [InlineData("/v1/auth/discord/callback", "GET")]
    [InlineData("/v1/auth/discord/authorize/invalid", "GET")]
    [InlineData("/health", "GET")]
    public async Task OtherEndpointsRemainOutsideWebClientPolicy(string path, string method)
    {
        await using var factory = new IdentityFactory { WebClientOrigins = [SiteOrigin] };
        using var client = factory.BrowserClient();
        using var response = await PreflightAsync(client, path, SiteOrigin, method);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task PendingExchangeExposesRetryAfterWithoutCookieCredentials()
    {
        await using var factory = new IdentityFactory { WebClientOrigins = [SiteOrigin] };
        using var client = factory.BrowserClient(cookies: false);
        client.DefaultRequestHeaders.Add("Origin", SiteOrigin);
        var verifier = new string('a', 43);
        using var challengeResponse = await client.PostAsJsonAsync("/v1/auth/discord/challenges", new { codeChallenge = IdentityService.CodeChallenge(verifier) });
        var challenge = (await challengeResponse.Content.ReadFromJsonAsync<ChallengeResult>())!;
        using var pending = await client.PostAsJsonAsync("/v1/auth/discord/exchange", new { challengeId = challenge.ChallengeId, codeVerifier = verifier });

        Assert.Equal(HttpStatusCode.Accepted, pending.StatusCode);
        Assert.Equal(SiteOrigin, Assert.Single(pending.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Contains("Retry-After", HeaderItems(pending, "Access-Control-Expose-Headers"));
        Assert.Equal(TimeSpan.FromSeconds(5), pending.Headers.RetryAfter!.Delta);
        Assert.False(pending.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Theory]
    [InlineData("*")]
    [InlineData("https://*.example.com")]
    [InlineData("not-an-origin")]
    [InlineData("https://example.com/")]
    [InlineData("https://example.com/path")]
    [InlineData("https://example.com\\")]
    [InlineData("https://user:password@example.com")]
    [InlineData("https://example.com?tracking=1")]
    [InlineData("https://example.com#fragment")]
    [InlineData("http://example.com")]
    [InlineData(" https://example.com")]
    public async Task InvalidOriginConfigurationFailsStartup(string origin)
    {
        await using var factory = new IdentityFactory { WebClientOrigins = [origin] };
        var error = Assert.Throws<OptionsValidationException>(() => factory.BrowserClient());
        Assert.Equal(typeof(WebClientOptions), error.OptionsType);
    }

    [Theory]
    [InlineData("http://localhost:51806", "Development", true)]
    [InlineData("http://127.0.0.1:51806", "Development", true)]
    [InlineData("http://localhost:51806", "Production", false)]
    [InlineData("http://127.0.0.1:51806", "Production", false)]
    [InlineData("http://localhost.attacker.example:51806", "Development", false)]
    [InlineData(SiteOrigin, "Production", true)]
    public async Task HttpLoopbackIsAllowedOnlyInDevelopment(string origin, string environment, bool allowed)
    {
        await using var factory = new IdentityFactory { WebClientOrigins = [origin], EnvironmentName = environment };
        if (!allowed)
        {
            var error = Assert.Throws<OptionsValidationException>(() => factory.BrowserClient());
            Assert.Equal(typeof(WebClientOptions), error.OptionsType);
            return;
        }
        using var client = factory.BrowserClient();
        using var response = await PreflightAsync(client, "/v1/auth/me", origin, "GET");
        Assert.Equal(origin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
    }

    private static async Task<HttpResponseMessage> PreflightAsync(HttpClient client, string path, string origin, string method)
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, path);
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", method);
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");
        return await client.SendAsync(request);
    }

    private static string[] HeaderItems(HttpResponseMessage response, string name) =>
        response.Headers.GetValues(name).SelectMany(value => value.Split(',', StringSplitOptions.TrimEntries)).ToArray();
}
