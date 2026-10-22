using System.Net;

namespace Arkya.Gateway.Tests;

public sealed class GatewayContentTests
{
    [Theory]
    [InlineData("GET", "/v1/content/posts?language=fr&limit=12")]
    [InlineData("GET", "/v1/content/posts/ab1890c5-9930-4ac0-bafd-cd15a48bceca")]
    [InlineData("OPTIONS", "/v1/content/posts")]
    [InlineData("OPTIONS", "/v1/content/posts/ab1890c5-9930-4ac0-bafd-cd15a48bceca")]
    public async Task PublicReadRoutesPreserveQueryAndCorsHeadersButRemoveOperatorCredentials(string method, string path)
    {
        await using var downstream = await Downstream.StartAsync("content");
        await using var factory = Factory(downstream.Address, true);
        using var client = factory.Client();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Add("Origin", "https://arkya.online");
        request.Headers.Add("X-Arkya-Admin-Key", "must-not-be-forwarded");
        request.Headers.Add("X-Arkya-Service-Key", "must-not-be-forwarded");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var received = Assert.Single(downstream.Requests);
        Assert.Equal(path, received.Path + received.Query);
        Assert.Equal("https://arkya.online", received.Headers["Origin"]);
        Assert.False(received.Headers.ContainsKey("X-Arkya-Admin-Key"));
        Assert.False(received.Headers.ContainsKey("X-Arkya-Service-Key"));
    }

    [Theory]
    [InlineData(false, "GET", "/v1/content/posts")]
    [InlineData(true, "POST", "/v1/content/posts")]
    [InlineData(true, "PUT", "/v1/content/posts/ab1890c5-9930-4ac0-bafd-cd15a48bceca")]
    [InlineData(true, "GET", "/admin/content/posts")]
    [InlineData(true, "POST", "/admin/content/posts/ab1890c5-9930-4ac0-bafd-cd15a48bceca/publish")]
    [InlineData(true, "GET", "/internal/content/catalogs/ab1890c5-9930-4ac0-bafd-cd15a48bceca")]
    public async Task WritesAndInternalRoutesCannotReachContentThroughPublicGateway(bool enabled, string method, string path)
    {
        await using var downstream = await Downstream.StartAsync("content");
        await using var factory = Factory(downstream.Address, enabled);
        using var client = factory.Client();
        using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
        Assert.Empty(downstream.Requests);
    }

    private static GatewayFactory Factory(string address, bool enabled) => new(address, overrides: new()
    {
        ["Gateway:ContentEnabled"] = enabled.ToString(),
        ["ReverseProxy:Clusters:content:Destinations:primary:Address"] = address
    });
}
