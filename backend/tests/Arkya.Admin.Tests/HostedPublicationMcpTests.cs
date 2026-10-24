using System.Net;
using System.Text.Json;
using Arkya.Admin.Reports;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Arkya.Admin.Tests;

public sealed class HostedPublicationMcpTests
{
    private static readonly string[] PublicationNames = ["create_publication", "delete_publication", "get_publication",
        "list_publications", "publish_publication", "unpublish_publication", "update_publication"];

    [Theory]
    [InlineData(true, false, 5)]
    [InlineData(false, true, 7)]
    [InlineData(true, true, 12)]
    public async Task DiscoveryExposesOnlyEnabledBackends(bool reports, bool content, int count)
    {
        await using var factory = Factory(reports, content);
        using var browser = Browser(factory);
        await using var mcp = await Connect(browser);
        var tools = await mcp.ListToolsAsync();
        Assert.Equal(count, tools.Count);
        Assert.Equal(content ? PublicationNames : [], tools.Select(tool => tool.Name)
            .Where(name => name.EndsWith("publication", StringComparison.Ordinal) || name == "list_publications").Order());
        Assert.Empty(factory.Content.Calls);
        Assert.Empty(factory.Reports.Calls);
        if (!content)
        {
            await Assert.ThrowsAsync<McpProtocolException>(() => mcp.CallToolAsync("create_publication", Fields("news")).AsTask());
            Assert.Empty(factory.Content.Calls);
        }
    }

    [Theory]
    [InlineData("news")]
    [InlineData("release-note")]
    public async Task HttpClientManagesDraftLifecycleWithServerOwnedContentCredentials(string kind)
    {
        await using var factory = Factory(false, true);
        using var browser = Browser(factory);
        await using var mcp = await Connect(browser);
        var create = await mcp.CallToolAsync("create_publication", Fields(kind));
        var created = Success(create);
        Assert.Equal(1, created.GetProperty("revision").GetInt64());
        Assert.False(created.TryGetProperty("publishedAt", out var createdPublished) && createdPublished.ValueKind != JsonValueKind.Null);
        var id = created.GetProperty("id").GetGuid();
        Assert.Single(factory.Content.Calls);
        Assert.Equal(HttpMethod.Post, factory.Content.Calls[0].Method);
        Assert.Equal("/admin/content/posts", factory.Content.Calls[0].Path);
        Assert.Equal(kind == "news" ? null : "2026.10.24", factory.Content.Post.GameVersion);

        var list = Success(await mcp.CallToolAsync("list_publications", new Dictionary<string, object?>
        {
            ["kind"] = kind, ["language"] = "fr", ["offset"] = 10, ["limit"] = 5
        }));
        Assert.Equal(id, list.GetProperty("items")[0].GetProperty("id").GetGuid());
        var query = Assert.Single(factory.Content.PostQueries);
        Assert.Contains("kind=" + kind, query);
        Assert.Contains("language=fr", query);
        Assert.Contains("offset=10", query);
        Assert.Contains("limit=5", query);
        var read = Success(await mcp.CallToolAsync("get_publication", new Dictionary<string, object?> { ["id"] = id }));
        Assert.Equal(1, read.GetProperty("revision").GetInt64());

        var publish = Success(await mcp.CallToolAsync("publish_publication", Revision(id, 1)));
        Assert.Equal(2, publish.GetProperty("publishedRevision").GetInt64());
        Assert.Equal(2, publish.GetProperty("revision").GetInt64());
        var updateFields = Fields(kind);
        updateFields["id"] = id;
        updateFields["expectedRevision"] = 2;
        updateFields["title"] = "Next draft";
        var updated = Success(await mcp.CallToolAsync("update_publication", updateFields));
        Assert.Equal(3, updated.GetProperty("revision").GetInt64());
        Assert.Equal(2, updated.GetProperty("publishedRevision").GetInt64());
        var save = Assert.Single(factory.Content.Calls, call => call.Method == HttpMethod.Put);
        Assert.Equal(2, save.Body!.Value.GetProperty("revision").GetInt64());
        Assert.False(save.Body.Value.TryGetProperty("expectedRevision", out _));

        var stale = await mcp.CallToolAsync("publish_publication", Revision(id, 2));
        Assert.True(stale.IsError);
        Assert.Equal(3, factory.Content.Post.Revision);
        AdminFactory.NoSecrets(Assert.Single(stale.Content.OfType<TextContentBlock>()).Text);
        var unpublish = Success(await mcp.CallToolAsync("unpublish_publication", Revision(id, 3)));
        Assert.Equal(4, unpublish.GetProperty("revision").GetInt64());
        Assert.False(unpublish.TryGetProperty("publishedAt", out var withdrawnPublished) && withdrawnPublished.ValueKind != JsonValueKind.Null);
        Success(await mcp.CallToolAsync("delete_publication", Revision(id, 4)));
        Assert.True(factory.Content.PostDeleted);
        Assert.Single(factory.Content.Calls, call => call.Method == HttpMethod.Delete);
        var missing = await mcp.CallToolAsync("get_publication", new Dictionary<string, object?> { ["id"] = id });
        Assert.True(missing.IsError);
        Assert.Empty(factory.Reports.Calls);
    }

    [Fact]
    public async Task UpstreamErrorsDoNotExposeContentCredentials()
    {
        await using var factory = Factory(false, true);
        factory.Content.WriteError = HttpStatusCode.Forbidden;
        using var browser = Browser(factory);
        await using var mcp = await Connect(browser);
        var denied = await mcp.CallToolAsync("create_publication", Fields("news"));
        Assert.True(denied.IsError);
        var text = Assert.Single(denied.Content.OfType<TextContentBlock>()).Text;
        AdminFactory.NoSecrets(text);
        Assert.DoesNotContain("private upstream details", text);
    }

    private static Dictionary<string, object?> Fields(string kind) => new()
    {
        ["kind"] = kind, ["language"] = "fr", ["title"] = "Publication test",
        ["summary"] = "Résumé", ["body"] = "Texte brut.\nDeuxième ligne.",
        ["gameVersion"] = kind == "release-note" ? "2026.10.24" : null
    };

    private static Dictionary<string, object?> Revision(Guid id, long revision) => new()
    {
        ["id"] = id, ["expectedRevision"] = revision
    };

    private static JsonElement Success(CallToolResult result)
    {
        Assert.NotEqual(true, result.IsError);
        var text = Assert.Single(result.Content.OfType<TextContentBlock>()).Text;
        AdminFactory.NoSecrets(text);
        using var json = JsonDocument.Parse(text);
        return json.RootElement.Clone();
    }

    private static AdminFactory Factory(bool reports, bool content)
    {
        var factory = new AdminFactory();
        factory.Settings["AdminMcp:Enabled"] = "true";
        factory.Settings["ReportsApi:Enabled"] = reports.ToString();
        factory.Settings["ContentApi:Enabled"] = content.ToString();
        return factory;
    }

    private static HttpClient Browser(AdminFactory factory)
    {
        var browser = factory.Browser();
        browser.DefaultRequestHeaders.Authorization = new("Bearer", AdminFactory.McpKey);
        return browser;
    }

    private static async Task<McpClient> Connect(HttpClient browser) => await McpClient.CreateAsync(new HttpClientTransport(
        new HttpClientTransportOptions
        {
            Endpoint = new Uri("https://localhost" + HostedReportsMcp.EndpointPath),
            TransportMode = HttpTransportMode.StreamableHttp
        }, browser));
}
