using System.Net;
using Arkya.Admin.Content;
using Microsoft.Extensions.Options;

namespace Arkya.Admin.Tests;

public sealed class ClusterHttpTests
{
    [Theory]
    [InlineData("ContentApi")]
    [InlineData("RealmsApi")]
    public async Task ProductionRequiresExplicitClusterHttpOptIn(string section)
    {
        const string url = "http://arkya-service.arkya-beta.svc.cluster.local:8080/";
        using (var denied = new AdminFactory { EnvironmentName = "Production" })
        {
            denied.Settings[$"{section}:Enabled"] = "true";
            denied.Settings[$"{section}:BaseUrl"] = url;
            Assert.Throws<OptionsValidationException>(() => denied.Browser());
        }
        await using var allowed = new AdminFactory { EnvironmentName = "Production" };
        allowed.Settings[$"{section}:Enabled"] = "true";
        allowed.Settings[$"{section}:BaseUrl"] = url;
        allowed.Settings[$"{section}:AllowClusterHttp"] = "true";
        using var browser = allowed.Browser();
        await AdminFactory.Login(browser);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync(section == "ContentApi" ? "/" : "/Realms")).StatusCode);
        Assert.NotEmpty(section == "ContentApi" ? allowed.Content.Calls.Select(call => call.Path) :
            allowed.Realms.Calls.Select(call => call.Path));
    }

    [Theory]
    [InlineData("http://arkya-content.arkya-beta.svc.cluster.local")]
    [InlineData("http://arkya-content.arkya-beta.svc.cluster.local:8080/")]
    public void ExplicitClusterHttpAcceptsOnlyQualifiedServiceRoots(string url) =>
        Assert.True(ContentApiOptions.IsSafe(url, development: false, allowClusterHttp: true));

    [Theory]
    [InlineData("http://external.example/")]
    [InlineData("http://127.0.0.1:8080/")]
    [InlineData("http://192.168.100.21:8080/")]
    [InlineData("http://169.254.169.254/")]
    [InlineData("http://arkya-content/")]
    [InlineData("http://arkya-content.arkya-beta.svc/")]
    [InlineData("http://arkya-content.arkya-beta.svc.cluster.local.attacker.example/")]
    [InlineData("http://arkya-content.arkya-beta.svc.cluster.local./")]
    [InlineData("http://-content.arkya-beta.svc.cluster.local/")]
    [InlineData("http://content.-arkya.svc.cluster.local/")]
    [InlineData("http://content.arkya.svc.cluster.local:0/")]
    [InlineData("http://content.arkya.svc.cluster.local:65536/")]
    [InlineData("http://key@arkya-content.arkya-beta.svc.cluster.local/")]
    [InlineData("http://arkya-content.arkya-beta.svc.cluster.local/api/")]
    [InlineData("http://arkya-content.arkya-beta.svc.cluster.local/../")]
    [InlineData("http://arkya-content.arkya-beta.svc.cluster.local/%2f")]
    [InlineData("http://arkya-content.arkya-beta.svc.cluster.local/?token=secret")]
    [InlineData("http://arkya-content.arkya-beta.svc.cluster.local/#fragment")]
    [InlineData("http://arkya-content.arkya-beta.svc.cluster.local/\n")]
    public void ClusterOptInDoesNotPermitOtherCleartextDestinations(string url) =>
        Assert.False(ContentApiOptions.IsSafe(url, development: false, allowClusterHttp: true));

    [Theory]
    [InlineData("https://content.example/", false, true)]
    [InlineData("http://localhost:5143", false, false)]
    [InlineData("http://localhost:5143", true, true)]
    [InlineData("http://external.example/", true, false)]
    public void ExistingHttpsAndLocalDevelopmentRulesRemain(string url, bool development, bool accepted) =>
        Assert.Equal(accepted, ContentApiOptions.IsSafe(url, development));
}
