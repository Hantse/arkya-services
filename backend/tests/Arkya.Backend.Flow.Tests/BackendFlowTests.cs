using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Arkya.Characters.Api.Application;
using Arkya.Characters.Api.Infrastructure.Identity;
using Arkya.Identity.Api.Application;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Xunit;

namespace Arkya.Backend.Flow.Tests;

public sealed class BackendFlowTests
{
    private const string IdentityKey = "test-identity-inbound-key-32-characters";
    private const string CharacterKey = "test-characters-inbound-key-32-characters";

    [MongoFlowFact]
    public async Task DiscordLoginCharacterCreationDedicatedResolveAndLogoutCrossBothServices()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var identityDatabase = $"arkya_flow_identity_test_{suffix}";
        var characterDatabase = $"arkya_flow_characters_test_{suffix}";
        var connectionString = Environment.GetEnvironmentVariable("ARKYA_TEST_MONGO")!;
        var mongo = new MongoClient(connectionString);
        try
        {
            using var identity = new WebApplicationFactory<global::Program>().WithWebHostBuilder(builder =>
            {
                Configure(builder, connectionString, identityDatabase, IdentityKey);
                builder.UseSetting("Discord:ClientId", "123456789");
                builder.UseSetting("Discord:ClientSecret", "test-provider-secret");
                builder.ConfigureServices(services => services.AddSingleton<IDiscordIdentityClient, StubDiscord>());
            });
            using var identityHttp = identity.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = false });
            using var characters = new WebApplicationFactory<Arkya.Characters.Api.Program>().WithWebHostBuilder(builder =>
            {
                Configure(builder, connectionString, characterDatabase, CharacterKey);
                builder.UseSetting("Identity:BaseUrl", "https://localhost/");
                builder.UseSetting("Identity:ServiceKey", IdentityKey);
                builder.ConfigureServices(services => services.AddHttpClient<ISessionValidator, IdentitySessionValidator>()
                    .ConfigurePrimaryHttpMessageHandler(() => identity.Server.CreateHandler()));
            });
            using var characterHttp = characters.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
            var verifier = new string('v', 64);
            using var challengeResponse = await identityHttp.PostAsJsonAsync("/v1/auth/discord/challenges", new { codeChallenge = IdentityService.CodeChallenge(verifier) });
            challengeResponse.EnsureSuccessStatusCode();
            var challenge = await challengeResponse.Content.ReadFromJsonAsync<ChallengeResult>();
            using var authorize = await identityHttp.GetAsync(new Uri(challenge!.AuthorizationUrl).PathAndQuery);
            Assert.Equal(HttpStatusCode.Redirect, authorize.StatusCode);
            var state = QueryHelpers.ParseQuery(authorize.Headers.Location!.Query)["state"].ToString();
            using var callbackRequest = new HttpRequestMessage(HttpMethod.Get, $"/v1/auth/discord/callback?state={state}&code=test-code");
            callbackRequest.Headers.Add("Cookie", authorize.Headers.GetValues("Set-Cookie").Single().Split(';')[0]);
            using var callback = await identityHttp.SendAsync(callbackRequest);
            Assert.Equal(HttpStatusCode.OK, callback.StatusCode);
            using var exchange = await identityHttp.PostAsJsonAsync("/v1/auth/discord/exchange", new { challengeId = challenge.ChallengeId, codeVerifier = verifier });
            exchange.EnsureSuccessStatusCode();
            var session = (await exchange.Content.ReadFromJsonAsync<SessionResult>())!;

            characterHttp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.SessionToken);
            using var create = await characterHttp.PostAsJsonAsync("/v1/characters", new { name = "Aeria" });
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);
            var created = await create.Content.ReadFromJsonAsync<JsonElement>();
            var characterId = created.GetProperty("id").GetString();
            Assert.Equal(1, created.GetProperty("level").GetInt32());
            using var resolveRequest = new HttpRequestMessage(HttpMethod.Post, $"/internal/characters/{characterId}/resolve")
            {
                Content = JsonContent.Create(new { sessionToken = session.SessionToken })
            };
            resolveRequest.Headers.Add("X-Arkya-Service-Key", CharacterKey);
            using var resolve = await characterHttp.SendAsync(resolveRequest);
            Assert.Equal(HttpStatusCode.OK, resolve.StatusCode);
            var resolved = await resolve.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(Guid.Parse(session.AccountId), resolved.GetProperty("accountId").GetGuid());

            identityHttp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.SessionToken);
            using var logout = await identityHttp.PostAsync("/v1/auth/logout", null);
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
            using var listAfterLogout = await characterHttp.GetAsync("/v1/characters");
            Assert.Equal(HttpStatusCode.Unauthorized, listAfterLogout.StatusCode);
        }
        finally
        {
            await mongo.DropDatabaseAsync(identityDatabase);
            await mongo.DropDatabaseAsync(characterDatabase);
        }
    }

    private static void Configure(IWebHostBuilder builder, string connectionString, string databaseName, string key)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Mongo:ConnectionString", connectionString);
        builder.UseSetting("Mongo:DatabaseName", databaseName);
        builder.UseSetting("ServiceAuthentication:Key", key);
    }

    private sealed class StubDiscord : IDiscordIdentityClient
    {
        public Task<DiscordUser> ExchangeCodeAsync(string code, CancellationToken cancellationToken) =>
            Task.FromResult(new DiscordUser("123456789012345678", "Test Player"));
    }
}

public sealed class MongoFlowFactAttribute : FactAttribute
{
    public MongoFlowFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ARKYA_TEST_MONGO")))
        {
            Skip = "Set ARKYA_TEST_MONGO to run the two-service flow against disposable MongoDB databases.";
        }
    }
}
