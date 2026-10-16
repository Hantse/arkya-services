using System.Net;
using System.Text;
using Arkya.Identity.Api.Application;
using Arkya.Identity.Api.Infrastructure;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Arkya.Identity.Tests;

public sealed class DiscordIdentityClientTests
{
    [Theory]
    [InlineData("{\"id\":\"123456789\",\"username\":\"username\",\"global_name\":\"Display Name\"}", "Display Name")]
    [InlineData("{\"id\":\"123456789\",\"username\":\"username\",\"global_name\":null}", "username")]
    public async Task ExchangesCodeAsFormThenFetchesProfileWithBearer(string profile, string expectedName)
    {
        var calls = 0;
        using var handler = new DelegateHttpHandler(async (request, ct) =>
        {
            calls++;
            if (calls == 1)
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("https://discord.com/api/oauth2/token", request.RequestUri!.AbsoluteUri);
                Assert.Equal("application/x-www-form-urlencoded", request.Content!.Headers.ContentType!.MediaType);
                var form = QueryHelpers.ParseQuery(await request.Content.ReadAsStringAsync(ct));
                Assert.Equal("authorization_code", form["grant_type"]);
                Assert.Equal("code with spaces&symbols", form["code"]);
                Assert.Equal("discord-client", form["client_id"]);
                Assert.Equal("server-only-secret", form["client_secret"]);
                Assert.Equal("https://identity.example/v1/auth/discord/callback", form["redirect_uri"]);
                Assert.Null(request.Headers.Authorization);
                return Json("{\"access_token\":\"discord-provider-token\",\"refresh_token\":\"unused-refresh-token\",\"scope\":\"identify\"}");
            }
            Assert.Equal(2, calls);
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("https://discord.com/api/v10/users/@me", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("discord-provider-token", request.Headers.Authorization.Parameter);
            Assert.Null(request.Content);
            return Json(profile);
        });
        using var http = new HttpClient(handler);
        var client = new DiscordIdentityClient(http, DiscordOptions());
        var result = await client.ExchangeCodeAsync("code with spaces&symbols", CancellationToken.None);
        Assert.Equal(new DiscordUser("123456789", expectedName), result);
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{}")]
    [InlineData("{\"access_token\":null}")]
    [InlineData("{\"access_token\":\" \"}")]
    public async Task InvalidTokenResponseCannotBecomeAnIdentity(string tokenResponse)
    {
        var calls = 0;
        using var handler = new DelegateHttpHandler((_, _) =>
        {
            calls++;
            return Task.FromResult(Json(tokenResponse));
        });
        using var http = new HttpClient(handler);
        var client = new DiscordIdentityClient(http, DiscordOptions());
        await Assert.ThrowsAsync<InvalidDataException>(() => client.ExchangeCodeAsync("code", CancellationToken.None));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"username\":\"Name\"}")]
    [InlineData("{\"id\":\"not-a-discord-id\",\"username\":\"Name\"}")]
    [InlineData("{\"id\":\"123\",\"username\":\"\"}")]
    [InlineData("{\"id\":123,\"username\":\"Name\"}")]
    public async Task InvalidProviderProfileIsRejected(string profile)
    {
        var calls = 0;
        using var handler = new DelegateHttpHandler((_, _) => Task.FromResult(Json(++calls == 1 ? "{\"access_token\":\"token\"}" : profile)));
        using var http = new HttpClient(handler);
        var client = new DiscordIdentityClient(http, DiscordOptions());
        await Assert.ThrowsAsync<InvalidDataException>(() => client.ExchangeCodeAsync("code", CancellationToken.None));
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProviderHttpFailureIsPropagated(bool profileFails)
    {
        var calls = 0;
        using var handler = new DelegateHttpHandler((_, _) =>
        {
            calls++;
            return Task.FromResult(profileFails && calls == 1
                ? Json("{\"access_token\":\"token\"}")
                : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        });
        using var http = new HttpClient(handler);
        var client = new DiscordIdentityClient(http, DiscordOptions());
        await Assert.ThrowsAsync<HttpRequestException>(() => client.ExchangeCodeAsync("code", CancellationToken.None));
        Assert.Equal(profileFails ? 2 : 1, calls);
    }

    private static IOptions<DiscordOptions> DiscordOptions() => Options.Create(new DiscordOptions
    {
        ClientId = "discord-client", ClientSecret = "server-only-secret", RedirectUri = "https://identity.example/v1/auth/discord/callback"
    });

    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value, Encoding.UTF8, "application/json") };

    private sealed class DelegateHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
