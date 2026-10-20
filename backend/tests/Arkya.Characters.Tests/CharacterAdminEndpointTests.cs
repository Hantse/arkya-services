using System.Net;
using System.Net.Http.Json;
using Arkya.Characters.Api.Application;
using Arkya.Characters.Api.Infrastructure;
using Arkya.ServiceDefaults;
using Arkya.ServiceDefaults.Characters;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arkya.Characters.Tests;

internal sealed class TestCharacterAdminRepository : ICharacterAdminRepository
{
    public CharacterAdminQuery? Query { get; private set; }
    public DateTimeOffset? ObservedAt { get; private set; }
    public Task<AdminCharacterPage> ListAsync(CharacterAdminQuery query, DateTimeOffset now, CancellationToken cancellationToken)
    {
        Query = query;
        ObservedAt = now;
        return Task.FromResult(new AdminCharacterPage([], 0, query.Page, query.PageSize));
    }
    public Task<AdminCharacterOverview> OverviewAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
        Task.FromResult(new AdminCharacterOverview(12, 3, [new("beta-web", 3)]));
}

public sealed class CharacterAdminEndpointTests
{
    private const string AdminKey = "characters-admin-test-key-thirty-two-characters";

    [Fact]
    public async Task CharacterAdministrationRequiresItsOwnKeyAndNeverUsesThePlayerSession()
    {
        await using var factory = Factory();
        using var client = factory.PlayerClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/admin/characters")).StatusCode);
        client.DefaultRequestHeaders.Add(ServiceKeyValidator.HeaderName, CharactersFactory.ServiceKey);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/admin/characters/overview")).StatusCode);
        client.DefaultRequestHeaders.Add(CharacterAdminKeyValidator.HeaderName, AdminKey);
        var response = await client.GetAsync("/admin/characters/overview");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        var overview = await response.Content.ReadFromJsonAsync<AdminCharacterOverview>();
        Assert.Equal(12, overview!.TotalCharacters);
        Assert.Equal(3, overview.OnlineCharacters);
        Assert.Equal(new RealmOnlineCount("beta-web", 3), Assert.Single(overview.Realms));
        Assert.Equal(0, factory.Identity.Calls);
    }

    [Fact]
    public async Task ListPassesOnlyBoundedValidatedFiltersAndUsesServerTime()
    {
        var repository = new TestCharacterAdminRepository();
        await using var factory = Factory(repository);
        using var client = factory.PlayerClient(null);
        client.DefaultRequestHeaders.Add(CharacterAdminKeyValidator.HeaderName, AdminKey);
        foreach (var query in new[] { "page=0", "page=100001", "pageSize=101", "accountId=not-a-uuid",
            "accountId=00000000-0000-0000-0000-000000000000", "onlineOnly=maybe", "search=" + new string('x', 101) })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/admin/characters?" + query)).StatusCode);
        Assert.Null(repository.Query);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/admin/characters?accountId=&search=&onlineOnly=false&page=1&pageSize=25")).StatusCode);
        Assert.Equal(new(null, "", false, 1, 25), repository.Query);
        var response = await client.GetAsync($"/admin/characters?accountId={IdentityStub.AliceId:D}&search=%20Aelys%20&onlineOnly=true&page=2&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new(IdentityStub.AliceId, "Aelys", true, 2, 10), repository.Query);
        Assert.Equal(FixedTimeProvider.Now, repository.ObservedAt);
    }

    [Fact]
    public async Task UnconfiguredAdministrationStaysDisabled()
    {
        await using var factory = new CharactersFactory();
        using var client = factory.PlayerClient(null);
        client.DefaultRequestHeaders.Add(CharacterAdminKeyValidator.HeaderName, AdminKey);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/admin/characters")).StatusCode);
    }

    [Theory]
    [InlineData("10.42.1.9", true)]
    [InlineData("::ffff:10.42.1.9", true)]
    [InlineData("203.0.113.9", false)]
    public async Task ProductionPrivateHttpChecksDirectPeerAndStillRequiresTheAdministratorKey(string peer, bool allowed)
    {
        await using var factory = Factory(production: true);
        foreach (var key in new[] { "wrong", AdminKey })
        {
            var response = await factory.Server.SendAsync(context =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
                context.Request.Host = new HostString("localhost");
                context.Request.Method = "GET";
                context.Request.Path = "/admin/characters/overview";
                context.Request.Scheme = "http";
                context.Request.Headers[CharacterAdminKeyValidator.HeaderName] = key;
                context.Request.Headers["X-Forwarded-Proto"] = "https";
                context.Request.Headers["X-Forwarded-For"] = "10.42.1.9";
            });
            Assert.Equal(allowed, response.IsArkyaInternalHttpAllowed());
            Assert.Equal(!allowed ? 400 : key == AdminKey ? 200 : 403, response.Response.StatusCode);
        }
    }

    [Theory]
    [InlineData("/v1/characters/appearance-options")]
    [InlineData("/internal/game/admissions/consume")]
    public async Task AdminPrivateHttpDoesNotRelaxOtherRoutes(string path)
    {
        await using var factory = Factory(production: true);
        var response = await factory.Server.SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse("10.42.1.9");
            context.Request.Host = new HostString("localhost");
            context.Request.Method = "GET";
            context.Request.Path = path;
            context.Request.Scheme = "http";
        });
        Assert.False(response.IsArkyaInternalHttpAllowed());
        Assert.Equal(400, response.Response.StatusCode);
    }

    [Theory]
    [InlineData("10.42.1.9", 200)]
    [InlineData("203.0.113.9", 400)]
    public async Task PrivateHealthProbeUsesTheDirectPeerAllowlist(string peer, int expectedStatus)
    {
        await using var factory = Factory(production: true);
        var response = await factory.Server.SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
            context.Request.Host = new HostString("localhost");
            context.Request.Method = "GET";
            context.Request.Path = "/health";
            context.Request.Scheme = "http";
            context.Request.Headers["X-Forwarded-For"] = "10.42.1.9";
        });
        Assert.Equal(expectedStatus, response.Response.StatusCode);
    }

    private static CharactersFactory Factory(TestCharacterAdminRepository? repository = null, bool production = false) => new()
    {
        EnvironmentName = production ? "Production" : "Development",
        AdditionalConfiguration = new Dictionary<string, string?>
        {
            ["AdminAuthentication:Key"] = AdminKey,
            ["InternalHttp:AllowedNetworks:0"] = "10.42.0.0/16"
        },
        ConfigureTestServices = services =>
        {
            services.RemoveAll<ICharacterAdminRepository>();
            services.AddSingleton<ICharacterAdminRepository>(repository ?? new TestCharacterAdminRepository());
        }
    };
}
