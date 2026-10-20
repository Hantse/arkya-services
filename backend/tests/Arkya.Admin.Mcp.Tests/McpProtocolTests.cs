using System.Collections.Concurrent;
using System.Text.Json;
using Arkya.ServiceDefaults.Reports;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Arkya.Admin.Mcp.Tests;

public sealed class McpProtocolTests
{
    [Theory]
    [InlineData(BugReportKinds.Bug, "list_bug_reports")]
    [InlineData(BugReportKinds.Suggestion, "list_suggestions")]
    public async Task Stdio_discovery_filters_image_and_revision_conflicts_work_end_to_end(string kind, string listTool)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var token = deadline.Token;
        var id = Guid.NewGuid();
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=");
        var key = new string('t', 40);
        var requests = new ConcurrentQueue<string>();
        var report = new BugReportResponse(1, id, Guid.NewGuid(), Guid.NewGuid(), "Stuck at gate",
            "I cannot move", "test-build", "StarterIsland", "WindowsPlayer", "new", ["urgent"], "", 1,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, new(1, 1, png.Length, "image/png", "test-hash"), [], kind);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["AllowedHosts"] = "*" });
        builder.Logging.ClearProviders();
        await using var api = builder.Build();
        api.Use(async (context, next) =>
        {
            Assert.Equal(key, context.Request.Headers["X-Arkya-Reports-Admin-Key"]);
            Assert.Equal("codex-test", context.Request.Headers["X-Arkya-Admin-Actor"]);
            requests.Enqueue(context.Request.Method + " " + context.Request.Path + context.Request.QueryString);
            await next();
        });
        api.MapGet("/internal/bug-reports", (HttpRequest request) =>
        {
            Assert.Equal("urgent", request.Query["tag"]);
            Assert.Equal("new", request.Query["status"]);
            Assert.Equal(kind, request.Query["kind"]);
            return new BugReportListResponse([report], 0, 20, 1);
        });
        api.MapGet("/internal/bug-reports/{reportId:guid}", (Guid reportId) =>
            reportId == id ? Results.Ok(report) : Results.NotFound());
        api.MapGet("/internal/bug-reports/{reportId:guid}/screenshot", (Guid reportId) =>
            reportId == id ? Results.Bytes(png, "image/png") : Results.NotFound());
        api.MapPatch("/internal/bug-reports/{reportId:guid}", (Guid reportId, UpdateBugReportRequest update) =>
        {
            if (reportId != id) return Results.NotFound();
            if (update.ExpectedRevision != report.Revision) return Results.Conflict();
            report = report with { Status = update.Status!, Tags = update.Tags!, Resolution = update.Resolution!, Revision = report.Revision + 1 };
            return Results.Ok(report);
        });
        await api.StartAsync(token);
        var address = api.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "arkya-admin-protocol-test",
            Command = "dotnet",
            Arguments = [typeof(Program).Assembly.Location],
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["Reports__BaseUrl"] = address, ["Reports__AdminKey"] = key,
                ["Reports__AllowLoopbackHttp"] = "true", ["Reports__Actor"] = "codex-test"
            }
        });
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: token);
        var tools = await client.ListToolsAsync(cancellationToken: token);
        Assert.Equal(new[] { "get_bug_report", "get_bug_report_screenshot", "list_bug_reports", "list_suggestions", "update_bug_report" },
            tools.Select(tool => tool.Name).Order().ToArray());
        var list = await client.CallToolAsync(listTool, new Dictionary<string, object?> { ["tag"] = "urgent", ["status"] = "new" }, cancellationToken: token);
        Assert.NotEqual(true, list.IsError);
        Assert.Contains("Stuck at gate", Assert.Single(list.Content.OfType<TextContentBlock>()).Text);
        using var listJson = JsonDocument.Parse(Assert.Single(list.Content.OfType<TextContentBlock>()).Text);
        Assert.Equal(kind, listJson.RootElement.GetProperty("items")[0].GetProperty("kind").GetString());
        var detail = await client.CallToolAsync("get_bug_report", new Dictionary<string, object?> { ["id"] = id.ToString() }, cancellationToken: token);
        Assert.NotEqual(true, detail.IsError);
        Assert.Equal(png, Assert.Single(detail.Content.OfType<ImageContentBlock>()).DecodedData.ToArray());
        var change = new Dictionary<string, object?>
        {
            ["id"] = id.ToString(), ["expectedRevision"] = 1, ["status"] = "in_progress",
            ["tags"] = new[] { "urgent" }, ["resolution"] = "Reproduced at the gate."
        };
        var update = await client.CallToolAsync("update_bug_report", change, cancellationToken: token);
        Assert.NotEqual(true, update.IsError);
        Assert.Equal(2, report.Revision);
        Assert.Equal(kind, report.Kind);
        var conflict = await client.CallToolAsync("update_bug_report", change, cancellationToken: token);
        Assert.True(conflict.IsError);
        Assert.Contains("Read it again", Assert.Single(conflict.Content.OfType<TextContentBlock>()).Text);
        Assert.Equal(2, report.Revision);
        Assert.Equal(5, requests.Count);
        await api.StopAsync(token);
    }

    [Theory]
    [InlineData("https://reports.example.org", false, true)]
    [InlineData("http://localhost:5145", true, true)]
    [InlineData("http://localhost:5145", false, false)]
    [InlineData("http://reports.example.org", true, false)]
    [InlineData("https://user:password@reports.example.org", false, false)]
    [InlineData("https://reports.example.org?key=secret", false, false)]
    [InlineData("https://reports.example.org/internal", false, false)]
    public void Operator_configuration_restricts_credential_destinations(string url, bool localHttp, bool valid)
    {
        var options = new ReportsMcpOptions { BaseUrl = url, AllowLoopbackHttp = localHttp, AdminKey = new string('a', 32) };
        Assert.Equal(valid, options.IsValid());
    }
}
