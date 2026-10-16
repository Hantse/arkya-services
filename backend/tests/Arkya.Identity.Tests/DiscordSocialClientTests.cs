using System.Net;
using System.Text;
using System.Text.Json;
using Arkya.Identity.Api.Application;
using Arkya.Identity.Api.Infrastructure;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Arkya.Identity.Tests;

public sealed class DiscordSocialClientTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExchangeAndRefreshUseConfidentialCredentialsAndVerifyProviderIdentity(bool refresh)
    {
        var calls = 0;
        using var handler = new DelegateHandler(async (request, cancellationToken) =>
        {
            calls++;
            if (calls == 1)
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("https://discord.com/api/v10/oauth2/token", request.RequestUri!.AbsoluteUri);
                Assert.Equal("Basic", request.Headers.Authorization!.Scheme);
                Assert.Equal("client-id:backend-secret", Encoding.UTF8.GetString(Convert.FromBase64String(request.Headers.Authorization.Parameter!)));
                Assert.Equal("application/x-www-form-urlencoded", request.Content!.Headers.ContentType!.MediaType);
                var form = QueryHelpers.ParseQuery(await request.Content.ReadAsStringAsync(cancellationToken));
                Assert.Equal(refresh ? "refresh_token" : "authorization_code", form["grant_type"]);
                if (refresh)
                {
                    Assert.Equal("original-refresh", form["refresh_token"]);
                    Assert.False(form.ContainsKey("code"));
                }
                else
                {
                    Assert.Equal("code&with symbols", form["code"]);
                    Assert.Equal(new string('v', 43), form["code_verifier"]);
                    Assert.Equal("http://127.0.0.1:6463/callback", form["redirect_uri"]);
                }
                return Json(ValidToken());
            }
            Assert.Equal("https://discord.com/api/v10/users/@me", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("provider-access", request.Headers.Authorization.Parameter);
            return Json("{\"id\":\"123456789\"}");
        });
        using var http = new HttpClient(handler);
        var client = CreateClient(http);
        var result = refresh
            ? await client.RefreshAsync("original-refresh", CancellationToken.None)
            : await client.ExchangeAsync("code&with symbols", new string('v', 43), "http://127.0.0.1:6463/callback", CancellationToken.None);
        Assert.Equal(new DiscordSocialGrant("provider-access", "provider-refresh", 604800, DiscordSocialService.RequiredScopes, "123456789"), result);
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData("identify")]
    [InlineData("openid sdk.social_layer_presence identify")]
    [InlineData("openid sdk.social_layer")]
    [InlineData("sdk.social_layer identify")]
    [InlineData("openid sdk.social_layer_extra identify")]
    public async Task MissingCommunicationOrIdentityScopeCannotProduceGrant(string scope)
    {
        var calls = 0;
        using var handler = new DelegateHandler((_, _) =>
        {
            calls++;
            return Task.FromResult(Json(ValidToken(scope)));
        });
        using var http = new HttpClient(handler);
        var failure = await Assert.ThrowsAsync<DiscordSocialException>(() => CreateClient(http).RefreshAsync("refresh", CancellationToken.None));
        Assert.Equal("discord_social_scope_required", failure.Code);
        Assert.Equal(403, failure.StatusCode);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{}")]
    [InlineData("{\"access_token\":\"secret-provider-data\"}")]
    public async Task MalformedResponsesExposeNoProviderData(string body)
    {
        using var handler = new DelegateHandler((_, _) => Task.FromResult(Json(body)));
        using var http = new HttpClient(handler);
        var failure = await Assert.ThrowsAsync<DiscordSocialException>(() => CreateClient(http).RefreshAsync("secret-refresh", CancellationToken.None));
        Assert.Equal("invalid_discord_response", failure.Message);
        Assert.Equal(502, failure.StatusCode);
        Assert.Null(failure.InnerException);
    }

    [Theory]
    [InlineData("access_token", "")]
    [InlineData("refresh_token", "")]
    [InlineData("token_type", "Bot")]
    public async Task InvalidTokenFieldsCannotReachProfileEndpoint(string field, string value)
    {
        var token = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(ValidToken())!;
        token[field] = JsonSerializer.SerializeToElement(value);
        var calls = 0;
        using var handler = new DelegateHandler((_, _) =>
        {
            calls++;
            return Task.FromResult(Json(JsonSerializer.Serialize(token)));
        });
        using var http = new HttpClient(handler);
        var failure = await Assert.ThrowsAsync<DiscordSocialException>(() => CreateClient(http).RefreshAsync("refresh", CancellationToken.None));
        Assert.Equal("invalid_discord_response", failure.Code);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(400, 400, "discord_authorization_rejected")]
    [InlineData(401, 400, "discord_authorization_rejected")]
    [InlineData(403, 400, "discord_authorization_rejected")]
    [InlineData(429, 503, "discord_unavailable")]
    [InlineData(500, 503, "discord_unavailable")]
    public async Task ProviderErrorsNeverExposeBodies(int providerStatus, int expectedStatus, string expectedCode)
    {
        using var handler = new DelegateHandler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)providerStatus)
        {
            Content = new StringContent("secret-provider-response")
        }));
        using var http = new HttpClient(handler);
        var failure = await Assert.ThrowsAsync<DiscordSocialException>(() => CreateClient(http).RefreshAsync("refresh", CancellationToken.None));
        Assert.Equal(expectedCode, failure.Code);
        Assert.Equal(expectedStatus, failure.StatusCode);
        Assert.Null(failure.InnerException);
    }

    [Fact]
    public async Task NetworkFailureIsSanitizedAndCallerCancellationStillPropagates()
    {
        using var networkHandler = new DelegateHandler((_, _) => throw new HttpRequestException("secret-provider-context"));
        using var networkHttp = new HttpClient(networkHandler);
        var failure = await Assert.ThrowsAsync<DiscordSocialException>(() => CreateClient(networkHttp).RefreshAsync("refresh", CancellationToken.None));
        Assert.Equal("discord_unavailable", failure.Code);
        Assert.Null(failure.InnerException);

        using var source = new CancellationTokenSource();
        using var cancelHandler = new DelegateHandler((_, cancellationToken) =>
        {
            source.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Json(ValidToken()));
        });
        using var cancelHttp = new HttpClient(cancelHandler);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateClient(cancelHttp).RefreshAsync("refresh", source.Token));
    }

    private static DiscordSocialClient CreateClient(HttpClient http) => new(http, Options.Create(new DiscordOptions
    {
        ClientId = "client-id", ClientSecret = "backend-secret"
    }));

    private static string ValidToken(string? scope = null) => JsonSerializer.Serialize(new
    {
        access_token = "provider-access", refresh_token = "provider-refresh", token_type = "Bearer", expires_in = 604800,
        scope = scope ?? DiscordSocialService.RequiredScopes
    });

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class DelegateHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
