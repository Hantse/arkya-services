using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Arkya.Gateway.Tests;

public sealed class GatewayInferenceTests
{
    [Theory]
    [InlineData("/agent/inference/heartbeat")]
    [InlineData("/agent/inference/enroll")]
    public async Task Enabled_agent_routes_preserve_authentication_and_strip_operator_keys(string path)
    {
        await using var downstream = await Downstream.StartAsync("inference");
        await using var factory = Factory(downstream.Address, true);
        using var client = factory.Client();
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "machine-credential");
        request.Headers.Add("X-Arkya-Admin-Key", "must-not-reach-control-plane");
        request.Content = new StringContent("{\"agentId\":\"windows-01\"}", Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var received = Assert.Single(downstream.Requests);
        Assert.Equal("Bearer machine-credential", received.Headers["Authorization"]);
        Assert.False(received.Headers.ContainsKey("X-Arkya-Admin-Key"));
        Assert.Equal("{\"agentId\":\"windows-01\"}", received.Body);
    }

    [Theory]
    [InlineData(false, "POST", "/agent/inference/heartbeat")]
    [InlineData(false, "POST", "/agent/inference/enroll")]
    [InlineData(true, "GET", "/agent/inference/heartbeat")]
    [InlineData(true, "GET", "/agent/inference/enroll")]
    [InlineData(true, "POST", "/agent/inference/arbitrary")]
    [InlineData(true, "GET", "/admin/inference/agents")]
    [InlineData(true, "POST", "/admin/inference/agents")]
    [InlineData(true, "POST", "/admin/inference/enrollment-grants")]
    [InlineData(true, "GET", "/admin/inference/models")]
    [InlineData(true, "PATCH", "/admin/inference/agents/windows-01/workers/worker-01")]
    [InlineData(true, "DELETE", "/admin/inference/agents/windows-01/workers/worker-01")]
    public async Task Only_enabled_agent_posts_are_public(bool enabled, string method, string path)
    {
        await using var downstream = await Downstream.StartAsync("inference");
        await using var factory = Factory(downstream.Address, enabled);
        using var client = factory.Client();
        using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
        Assert.Empty(downstream.Requests);
    }

    private static GatewayFactory Factory(string address, bool enabled) => new(address, overrides: new()
    {
        ["Gateway:InferenceEnabled"] = enabled.ToString(),
        ["ReverseProxy:Clusters:inference:Destinations:primary:Address"] = address
    });
}
