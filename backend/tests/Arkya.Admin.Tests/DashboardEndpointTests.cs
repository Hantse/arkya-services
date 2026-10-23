using System.Net;
using Arkya.Admin.Realms;

namespace Arkya.Admin.Tests;

public sealed class DashboardEndpointTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/Infrastructure")]
    public async Task OperationsRequireAnAdminSession(string path)
    {
        await using var factory = new AdminFactory();
        using var browser = factory.Browser();
        using var response = await browser.GetAsync(path);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Empty(factory.Realms.Calls);
        Assert.Empty(factory.Content.Calls);
    }

    [Fact]
    public async Task DashboardShowsLiveCountsAndGroupedNavigation()
    {
        await using var factory = new AccountsFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = WebUtility.HtmlDecode(await browser.GetStringAsync("/"));
        Assert.Contains("Online players", html);
        Assert.Contains("<strong>80</strong>", html);
        Assert.Contains("<strong>1</strong>", html);
        Assert.Contains("Community", html);
        Assert.Contains("Infrastructure", html);
        Assert.Contains("Content and support", html);
        Assert.Contains("href=\"/Quests\"", html);
        Assert.Contains("Not configured", html);
        Assert.DoesNotContain(AccountsStub.Key, html);
        Assert.Equal(2, factory.Upstream.Calls.Count);
        Assert.All(factory.Upstream.Calls, call => Assert.Equal(HttpMethod.Get, call.Method));
    }

    [Fact]
    public async Task FailedServiceDoesNotHideOtherCountsOrExposeUpstreamErrors()
    {
        await using var factory = new AccountsFactory();
        factory.Upstream.ReadError = HttpStatusCode.ServiceUnavailable;
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = WebUtility.HtmlDecode(await browser.GetStringAsync("/"));
        Assert.Contains("Unavailable", html);
        Assert.Contains("<strong>—</strong>", html);
        Assert.DoesNotContain("<strong>0</strong>", html);
        Assert.DoesNotContain(AccountsStub.Key, html);
        AdminFactory.NoSecrets(html);
    }

    [Fact]
    public async Task InfrastructureShowsTargetReservationAndPinnedProfilesWithoutMutatingTheCluster()
    {
        await using var factory = new AdminFactory();
        factory.Settings["RealmsApi:Enabled"] = "true";
        factory.Realms.Options = factory.Realms.Options with
        {
            Profiles = [factory.Realms.Options.Profiles[0] with
            {
                Servers = [new("starter-island", 7777, "WebSocket", "wss://game.example/")]
            }]
        };
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = WebUtility.HtmlDecode(await browser.GetStringAsync("/Infrastructure"));
        Assert.Contains("Deployer enabled", html);
        Assert.Contains("arkya-realms", html);
        Assert.Contains("Les Terres d'Aube", html);
        Assert.Contains("@sha256:", html);
        Assert.Contains("WebSocket / TCP", html);
        Assert.Contains("wss://game.example/", html);
        Assert.All(factory.Realms.Calls, call => Assert.Equal(HttpMethod.Get, call.Method));
        AdminFactory.NoSecrets(html);
    }

    [Fact]
    public async Task FailedRealmsDoesNotHideContentOrRenderADeploymentButton()
    {
        await using var factory = new AdminFactory();
        factory.Settings["RealmsApi:Enabled"] = "true";
        factory.Realms.ReadError = HttpStatusCode.ServiceUnavailable;
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var dashboard = WebUtility.HtmlDecode(await browser.GetStringAsync("/"));
        Assert.Contains("Available", dashboard);
        Assert.Contains("Unavailable", dashboard);
        var infrastructure = WebUtility.HtmlDecode(await browser.GetStringAsync("/Infrastructure"));
        Assert.DoesNotContain("Prepare a realm</a>", infrastructure);
        AdminFactory.NoSecrets(dashboard);
        AdminFactory.NoSecrets(infrastructure);
    }
}
