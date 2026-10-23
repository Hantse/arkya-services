using System.Net;
using System.Text;

namespace Arkya.Gateway.Tests;

public sealed class GatewayItemVisualExportTests
{
    private const string Guid = "1234567890abcdef1234567890abcdef";
    private const string Path = "/admin/content/item-assets/" + Guid;
    private const string ExportKey = "unity-export-private-test-key-123456";
    private const string Body = "{\"name\":\"Épée E1-A\",\"assetPath\":\"Assets/Arkya/Items/Weapons/Prefabs/E1-A.prefab\",\"kind\":\"prefab\",\"dependencyHash\":\"abcdef1234567890abcdef1234567890\",\"thumbnailPngBase64\":\"private-preview-bytes\"}";
    private static readonly string[] OtherOperatorHeaders =
        ["X-Arkya-Service-Key", "X-Arkya-Dedicated-Key", "X-Arkya-Reports-Admin-Key", "X-Arkya-Admin-Actor"];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Exact_central_admin_visual_put_preserves_export_key_and_body_only(bool trustedProxy)
    {
        await using var identity = await Downstream.StartAsync("identity");
        await using var admin = await Downstream.StartAsync("admin");
        await using var factory = new GatewayFactory(identity.Address, overrides: Enabled(admin.Address));
        var scheme = trustedProxy ? "http" : "https";
        using var client = factory.Client(scheme);
        using var request = Request("PUT", scheme + "://admin.arkya.online" + Path);
        if (trustedProxy)
        {
            request.Headers.Add("X-Forwarded-Proto", "https");
            request.Headers.Add("X-Forwarded-For", "203.0.113.7");
        }
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(identity.Requests);
        var received = Assert.Single(admin.Requests);
        Assert.Equal("PUT", received.Method);
        Assert.Equal(Path, received.Path);
        Assert.Equal(Body, received.Body);
        Assert.Equal("application/json; charset=utf-8", received.Headers["Content-Type"]);
        Assert.Equal(ExportKey, received.Headers["X-Arkya-Admin-Key"]);
        Assert.Equal("admin.arkya.online", received.Headers["Host"]);
        Assert.Equal("https", received.Headers["X-Forwarded-Proto"]);
        if (trustedProxy) Assert.Equal("203.0.113.7", received.Headers["X-Forwarded-For"]);
        AssertRemovedOtherOperatorHeaders(received);
        Assert.DoesNotContain(factory.Logs.Messages, message => message.Contains(ExportKey) || message.Contains("private-preview-bytes"));
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    [InlineData("OPTIONS")]
    public async Task Other_methods_do_not_forward_the_export_key(string method)
    {
        await using var admin = await Downstream.StartAsync("admin");
        await using var factory = new GatewayFactory(admin.Address, overrides: Enabled(admin.Address));
        using var client = factory.Client();
        using var response = await client.SendAsync(Request(method, "https://admin.arkya.online" + Path));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertAllOperatorHeadersRemoved(Assert.Single(admin.Requests));
    }

    [Theory]
    [InlineData("/admin/content/item-assets")]
    [InlineData("/admin/content/item-assets/")]
    [InlineData("/admin/content/item-assets/00000000000000000000000000000000")]
    [InlineData("/admin/content/item-assets/1234567890ABCDEF1234567890ABCDEF")]
    [InlineData("/admin/content/item-assets/1234567890abcdef1234567890abcdeg")]
    [InlineData("/admin/content/item-assets/1234567890abcdef1234567890abcdef0")]
    [InlineData(Path + "/")]
    [InlineData(Path + "/thumbnail")]
    [InlineData("/admin/content/items/draft")]
    [InlineData("/admin/content/items/catalogs")]
    [InlineData("/internal/content/items/catalogs/latest")]
    [InlineData("/Login")]
    public async Task Other_paths_and_malformed_guids_do_not_forward_the_export_key(string path)
    {
        await using var admin = await Downstream.StartAsync("admin");
        await using var factory = new GatewayFactory(admin.Address, overrides: Enabled(admin.Address));
        using var client = factory.Client();
        using var response = await client.SendAsync(Request("PUT", "https://admin.arkya.online" + path));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertAllOperatorHeadersRemoved(Assert.Single(admin.Requests));
    }

    [Theory]
    [InlineData("api.arkya.online", false)]
    [InlineData("arkya.online", true)]
    [InlineData("beta-one.arkya.online", true)]
    [InlineData("beta-one.admin.arkya.online", true)]
    public async Task Public_and_realm_hosts_never_forward_the_export_key(string host, bool routed)
    {
        await using var destination = await Downstream.StartAsync("destination");
        var settings = Enabled(destination.Address);
        settings["AllowedHosts"] = "api.arkya.online;arkya.online;admin.arkya.online;*.arkya.online;*.admin.arkya.online";
        settings["Gateway:FrontendEnabled"] = "true";
        settings["Gateway:FrontendHost"] = "arkya.online";
        settings["Gateway:FrontendAddress"] = destination.Address;
        settings["Gateway:RealmSitesEnabled"] = "true";
        settings["Gateway:RealmBaseDomain"] = "arkya.online";
        settings["Gateway:PublicRealmsEnabled"] = "true";
        settings["Gateway:PublicRealmsAddress"] = destination.Address;
        await using var factory = new GatewayFactory(destination.Address, overrides: settings);
        using var client = factory.Client();
        using var request = Request("PUT", "https://" + host + Path);
        request.Headers.Add("X-Forwarded-Host", "admin.arkya.online");
        using var response = await client.SendAsync(request);

        Assert.Equal(routed ? HttpStatusCode.OK : HttpStatusCode.NotFound, response.StatusCode);
        if (routed) AssertAllOperatorHeadersRemoved(Assert.Single(destination.Requests));
        else Assert.Empty(destination.Requests);
    }

    [Fact]
    public async Task Missing_export_key_is_not_invented_by_the_gateway()
    {
        await using var admin = await Downstream.StartAsync("admin");
        await using var factory = new GatewayFactory(admin.Address, overrides: Enabled(admin.Address));
        using var client = factory.Client();
        using var request = Request("PUT", "https://admin.arkya.online" + Path);
        request.Headers.Remove("X-Arkya-Admin-Key");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertAllOperatorHeadersRemoved(Assert.Single(admin.Requests));
    }

    private static Dictionary<string, string?> Enabled(string address) => new()
    {
        ["AllowedHosts"] = "api.arkya.online;admin.arkya.online",
        ["Gateway:AdminEnabled"] = "true",
        ["Gateway:AdminHost"] = "admin.arkya.online",
        ["Gateway:AdminAddress"] = address
    };

    private static HttpRequestMessage Request(string method, string url)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url)
        { Content = new StringContent(Body, Encoding.UTF8, "application/json") };
        request.Headers.Add("X-Arkya-Admin-Key", ExportKey);
        foreach (var header in OtherOperatorHeaders) request.Headers.Add(header, "must-still-be-removed");
        return request;
    }

    private static void AssertAllOperatorHeadersRemoved(ReceivedRequest request)
    {
        Assert.DoesNotContain("X-Arkya-Admin-Key", request.Headers.Keys);
        AssertRemovedOtherOperatorHeaders(request);
    }

    private static void AssertRemovedOtherOperatorHeaders(ReceivedRequest request)
    {
        foreach (var header in OtherOperatorHeaders) Assert.DoesNotContain(header, request.Headers.Keys);
    }
}
