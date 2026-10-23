using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Arkya.Gateway.Tests;

public sealed class GatewayReportsTests
{
    [Theory]
    [InlineData("POST", "/v1/bug-reports")]
    [InlineData("GET", "/v1/bug-reports?offset=20&limit=10")]
    [InlineData("GET", "/v1/bug-reports/ab1890c5-9930-4ac0-bafd-cd15a48bceca")]
    [InlineData("OPTIONS", "/v1/bug-reports")]
    public async Task Enabled_routes_preserve_player_payload_and_strip_operator_credentials(string method, string path)
    {
        await using var downstream = await Downstream.StartAsync("reports");
        await using var factory = Factory(downstream.Address, true);
        using var client = factory.Client();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "player-session");
        request.Headers.Add("X-Arkya-Reports-Admin-Key", "never-forward-this");
        request.Headers.Add("X-Arkya-Admin-Actor", "forged-actor");
        if (method == "POST") request.Content = new StringContent("{\"title\":\"Stuck\"}", Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var received = Assert.Single(downstream.Requests);
        Assert.Equal("Bearer player-session", received.Headers["Authorization"]);
        Assert.False(received.Headers.ContainsKey("X-Arkya-Reports-Admin-Key"));
        Assert.False(received.Headers.ContainsKey("X-Arkya-Admin-Actor"));
        Assert.Equal(path, received.Path + received.Query);
        if (method == "POST") Assert.Equal("{\"title\":\"Stuck\"}", received.Body);
    }

    [Theory]
    [InlineData(false, "POST", "/v1/bug-reports")]
    [InlineData(true, "GET", "/internal/bug-reports?tag=urgent")]
    [InlineData(true, "PATCH", "/v1/bug-reports/ab1890c5-9930-4ac0-bafd-cd15a48bceca")]
    [InlineData(true, "GET", "/v1/bug-reports/ab1890c5-9930-4ac0-bafd-cd15a48bceca/screenshot")]
    [InlineData(true, "GET", "/mcp")]
    public async Task Disabled_and_operator_routes_never_reach_service(bool enabled, string method, string path)
    {
        await using var downstream = await Downstream.StartAsync("reports");
        await using var factory = Factory(downstream.Address, enabled);
        using var client = factory.Client();
        using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
        Assert.Empty(downstream.Requests);
    }

    private static GatewayFactory Factory(string address, bool enabled) => new(address, overrides: new()
    {
        ["Gateway:ReportsEnabled"] = enabled.ToString(),
        ["ReverseProxy:Clusters:reports:Destinations:primary:Address"] = address
    });
}
