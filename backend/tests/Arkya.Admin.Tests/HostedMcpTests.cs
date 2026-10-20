using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Arkya.Admin.Reports;
using Arkya.ServiceDefaults.Reports;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Arkya.Admin.Tests;

public sealed class HostedMcpTests
{
    private static AdminFactory EnabledFactory()
    {
        var factory = new AdminFactory();
        factory.Settings["ReportsApi:Enabled"] = "true";
        factory.Settings["AdminMcp:Enabled"] = "true";
        return factory;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-key-with-enough-characters-123456")]
    [InlineData(AdminFactory.ReportsAdminKey)]
    [InlineData(AdminFactory.Password)]
    public async Task BearerKeyIsRequiredBeforeParsingProtocolRequests(string? key)
    {
        await using var factory = EnabledFactory();
        using var browser = factory.Browser();
        using var request = new HttpRequestMessage(HttpMethod.Post, HostedReportsMcp.EndpointPath)
        {
            Content = new StringContent("not even JSON")
        };
        if (key is not null) request.Headers.Authorization = new("Bearer", key);
        using var response = await browser.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.Single().Scheme);
        Assert.Empty(factory.Reports.Calls);
        AdminFactory.NoSecrets(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task OperatorCookieCannotReplaceTheMcpKey()
    {
        await using var factory = EnabledFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        using var response = await browser.PostAsJsonAsync(HostedReportsMcp.EndpointPath, new { jsonrpc = "2.0", id = 1, method = "tools/list" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(factory.Reports.Calls);
    }

    [Fact]
    public async Task McpBearerCannotAuthenticateTheBrowserUi()
    {
        await using var factory = EnabledFactory();
        using var browser = factory.Browser();
        browser.DefaultRequestHeaders.Authorization = new("Bearer", AdminFactory.McpKey);
        using var response = await browser.GetAsync("/BugReports");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Login?", response.Headers.Location!.OriginalString);
        Assert.Empty(factory.Reports.Calls);
    }

    [Fact]
    public async Task DisabledMcpIsNotMapped()
    {
        await using var factory = new AdminFactory();
        factory.Settings["AdminMcp:Key"] = "";
        using var browser = factory.Browser();
        using var response = await browser.PostAsJsonAsync(HostedReportsMcp.EndpointPath, new { jsonrpc = "2.0", id = 1, method = "tools/list" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(factory.Reports.Calls);
    }

    [Theory]
    [InlineData("https://attacker.example")]
    [InlineData("https://localhost.attacker.example")]
    [InlineData("http://localhost")]
    [InlineData("null")]
    [InlineData("https://localhost/private")]
    [InlineData("https://user@localhost")]
    public async Task ForeignOrMalformedOriginsAreRejected(string origin)
    {
        await using var factory = EnabledFactory();
        using var browser = factory.Browser();
        browser.DefaultRequestHeaders.Authorization = new("Bearer", AdminFactory.McpKey);
        browser.DefaultRequestHeaders.Add("Origin", origin);
        using var response = await browser.PostAsJsonAsync(HostedReportsMcp.EndpointPath, new { jsonrpc = "2.0", id = 1, method = "tools/list" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(factory.Reports.Calls);
    }

    [Fact]
    public async Task MultipleOriginsAreRejected()
    {
        await using var factory = EnabledFactory();
        using var browser = factory.Browser();
        browser.DefaultRequestHeaders.Authorization = new("Bearer", AdminFactory.McpKey);
        browser.DefaultRequestHeaders.TryAddWithoutValidation("Origin", ["https://localhost", "https://attacker.example"]);
        using var response = await browser.PostAsJsonAsync(HostedReportsMcp.EndpointPath, new { jsonrpc = "2.0", id = 1, method = "tools/list" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task InitializeHandshakeAndToolListWorkWithoutASession()
    {
        await using var factory = EnabledFactory();
        using var browser = factory.Browser();
        browser.DefaultRequestHeaders.Authorization = new("Bearer", AdminFactory.McpKey);
        browser.DefaultRequestHeaders.Add("Origin", "https://localhost");
        var initialize = await Rpc(browser, "initialize", new
        {
            protocolVersion = "2025-11-25", capabilities = new { }, clientInfo = new { name = "arkya-test", version = "1.0" }
        });
        Assert.Equal("2025-11-25", initialize.GetProperty("result").GetProperty("protocolVersion").GetString());
        var list = await Rpc(browser, "tools/list", new { });
        var names = list.GetProperty("result").GetProperty("tools").EnumerateArray()
            .Select(tool => tool.GetProperty("name").GetString()!).Order().ToArray();
        Assert.Equal(["get_bug_report", "get_bug_report_screenshot", "list_bug_reports", "list_suggestions", "update_bug_report"], names);
        Assert.Empty(factory.Reports.Calls);
        foreach (var path in new[] { "/mcp/reports/sse", "/mcp/reports/message", "/mcp/other" })
            Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync(path)).StatusCode);
    }

    [Theory]
    [InlineData(BugReportKinds.Bug, "list_bug_reports")]
    [InlineData(BugReportKinds.Suggestion, "list_suggestions")]
    public async Task OfficialHttpClientReadsImagesAndUpdatesWithServerOwnedCredentials(string kind, string listTool)
    {
        await using var factory = EnabledFactory();
        factory.Reports.Report = factory.Reports.Report with { Kind = kind };
        using var browser = factory.Browser();
        browser.DefaultRequestHeaders.Authorization = new("Bearer", AdminFactory.McpKey);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        await using var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri("https://localhost" + HostedReportsMcp.EndpointPath),
            TransportMode = HttpTransportMode.StreamableHttp
        }, browser);
        await using var mcp = await McpClient.CreateAsync(transport, cancellationToken: token);
        Assert.Equal(5, (await mcp.ListToolsAsync(cancellationToken: token)).Count);
        var list = await mcp.CallToolAsync(listTool, new Dictionary<string, object?>
        {
            ["tag"] = "urgent", ["status"] = "new"
        }, cancellationToken: token);
        Assert.NotEqual(true, list.IsError);
        var query = Assert.Single(factory.Reports.Calls).Uri.Query;
        Assert.Contains("tag=urgent", query);
        Assert.Contains("status=new", query);
        Assert.Contains("kind=" + kind, query);
        using var listJson = JsonDocument.Parse(Assert.Single(list.Content.OfType<TextContentBlock>()).Text);
        Assert.Equal(kind, listJson.RootElement.GetProperty("items")[0].GetProperty("kind").GetString());
        var detail = await mcp.CallToolAsync("get_bug_report", new Dictionary<string, object?>
        {
            ["id"] = ReportsStub.Id.ToString()
        }, cancellationToken: token);
        Assert.NotEqual(true, detail.IsError);
        Assert.Equal(factory.Reports.Screenshot, Assert.Single(detail.Content.OfType<ImageContentBlock>()).DecodedData.ToArray());
        var update = await mcp.CallToolAsync("update_bug_report", new Dictionary<string, object?>
        {
            ["id"] = ReportsStub.Id.ToString(), ["expectedRevision"] = 1, ["status"] = "in_progress",
            ["tags"] = new[] { "urgent" }, ["resolution"] = "En cours de vérification."
        }, cancellationToken: token);
        Assert.NotEqual(true, update.IsError);
        Assert.Equal(2, factory.Reports.Report.Revision);
        Assert.Equal(kind, factory.Reports.Report.Kind);
        Assert.All(factory.Reports.Calls, call => Assert.Equal("codex-hosted", call.Actor));
        var mutation = Assert.Single(factory.Reports.Calls, call => call.Method == HttpMethod.Patch);
        Assert.Equal(1, mutation.Body!.Value.GetProperty("expectedRevision").GetInt64());
        factory.Reports.WriteError = HttpStatusCode.Conflict;
        var conflict = await mcp.CallToolAsync("update_bug_report", new Dictionary<string, object?>
        {
            ["id"] = ReportsStub.Id.ToString(), ["expectedRevision"] = 1, ["status"] = "resolved",
            ["tags"] = new[] { "urgent" }, ["resolution"] = "Stale update."
        }, cancellationToken: token);
        Assert.True(conflict.IsError);
        Assert.Equal(2, factory.Reports.Report.Revision);
        AdminFactory.NoSecrets(Assert.Single(conflict.Content.OfType<TextContentBlock>()).Text);
    }

    [Theory]
    [InlineData("AdminMcp:Key", "too-short")]
    [InlineData("AdminMcp:Key", AdminFactory.ReportsAdminKey)]
    [InlineData("AdminMcp:Key", AdminFactory.Password)]
    [InlineData("ReportsApi:Enabled", "false")]
    public void UnsafeOrIncompleteConfigurationFailsStartup(string key, string value)
    {
        using var factory = EnabledFactory();
        factory.Settings[key] = value;
        Assert.Throws<OptionsValidationException>(() => factory.Browser());
    }

    [Fact]
    public async Task ProductionRejectsCleartextAndUntrustedForwardedHttps()
    {
        await using var factory = EnabledFactory();
        factory.EnvironmentName = "Production";
        using var browser = factory.Browser();
        using var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost" + HostedReportsMcp.EndpointPath)
        {
            Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method = "tools/list" })
        };
        request.Headers.Authorization = new("Bearer", AdminFactory.McpKey);
        request.Headers.Add("X-Forwarded-Proto", "https");
        using var response = await browser.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.Reports.Calls);
    }

    private static async Task<JsonElement> Rpc(HttpClient browser, string method, object parameters)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, HostedReportsMcp.EndpointPath)
        {
            Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method, @params = parameters })
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.Add("MCP-Protocol-Version", "2025-11-25");
        using var response = await browser.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Mcp-Session-Id"));
        Assert.True(response.Headers.CacheControl!.NoStore);
        var text = await response.Content.ReadAsStringAsync();
        AdminFactory.NoSecrets(text);
        var json = response.Content.Headers.ContentType?.MediaType == "text/event-stream"
            ? text.Split('\n').Single(line => line.StartsWith("data: ", StringComparison.Ordinal))[6..]
            : text;
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
