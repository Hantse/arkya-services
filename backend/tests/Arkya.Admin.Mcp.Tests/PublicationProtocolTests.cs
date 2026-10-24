using System.Collections.Concurrent;
using System.Text.Json;
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

public sealed class PublicationProtocolTests
{
    [Theory]
    [InlineData("news", null)]
    [InlineData("release-note", "2026.10.24-beta+1")]
    public async Task Stdio_publication_tools_preserve_drafts_revisions_and_private_credentials(string kind, string? version)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var token = deadline.Token;
        var key = new string('t', 40);
        var requests = new ConcurrentQueue<string>();
        StoredPost? post = null;
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["AllowedHosts"] = "*" });
        builder.Logging.ClearProviders();
        await using var api = builder.Build();
        api.Use(async (context, next) =>
        {
            Assert.Equal(key, Assert.Single(context.Request.Headers["X-Arkya-Admin-Key"]));
            Assert.False(context.Request.Headers.ContainsKey("X-Arkya-Reports-Admin-Key"));
            Assert.False(context.Request.Headers.ContainsKey("X-Arkya-Admin-Actor"));
            Assert.False(context.Request.Headers.ContainsKey("Authorization"));
            requests.Enqueue(context.Request.Method + " " + context.Request.Path + context.Request.QueryString);
            await next();
        });
        api.MapPost("/admin/content/posts", async (HttpRequest request) =>
        {
            using var json = await JsonDocument.ParseAsync(request.Body, cancellationToken: token);
            var fields = json.RootElement;
            Assert.False(fields.TryGetProperty("revision", out _));
            Assert.False(fields.TryGetProperty("publishedAt", out _));
            Assert.False(fields.TryGetProperty("publishedRevision", out _));
            post = ReadDraft(fields, new StoredPost(Guid.NewGuid(), 1, "", "", "", "", "", null,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null));
            return Results.Created($"/admin/content/posts/{post.Id}", post);
        });
        api.MapGet("/admin/content/posts", (HttpRequest request) =>
        {
            Assert.Equal(kind, request.Query["kind"]);
            Assert.Equal("fr", request.Query["language"]);
            Assert.Equal("20", request.Query["offset"]);
            Assert.Equal("10", request.Query["limit"]);
            return Results.Ok(new { items = new[] { post! }, hasMore = true });
        });
        api.MapGet("/admin/content/posts/{id:guid}", (Guid id) =>
            post is not null && post.Id == id ? Results.Ok(post) : Results.NotFound());
        api.MapPut("/admin/content/posts/{id:guid}", async (Guid id, HttpRequest request) =>
        {
            using var json = await JsonDocument.ParseAsync(request.Body, cancellationToken: token);
            if (post is null || id != post.Id) return Results.NotFound();
            if (json.RootElement.GetProperty("revision").GetInt64() != post.Revision) return Results.Conflict();
            Assert.False(json.RootElement.TryGetProperty("expectedRevision", out _));
            post = ReadDraft(json.RootElement, post) with { Revision = post.Revision + 1, UpdatedAt = DateTimeOffset.UtcNow };
            return Results.Ok(post);
        });
        foreach (var operation in new[] { "publish", "unpublish" })
        {
            api.MapPost("/admin/content/posts/{id:guid}/" + operation, async (Guid id, HttpRequest request) =>
            {
                using var json = await JsonDocument.ParseAsync(request.Body, cancellationToken: token);
                Assert.Equal("revision", Assert.Single(json.RootElement.EnumerateObject()).Name);
                if (post is null || id != post.Id) return Results.NotFound();
                if (json.RootElement.GetProperty("revision").GetInt64() != post.Revision) return Results.Conflict();
                post = post with
                {
                    Revision = post.Revision + 1,
                    PublishedRevision = operation == "publish" ? post.Revision + 1 : null,
                    PublishedAt = operation == "publish" ? DateTimeOffset.UtcNow : null
                };
                return Results.Ok(post);
            });
        }
        api.MapDelete("/admin/content/posts/{id:guid}", (Guid id, HttpRequest request) =>
        {
            if (post is null || id != post.Id) return Results.NotFound();
            Assert.False(request.Query.ContainsKey("expectedRevision"));
            if (request.Query["revision"] != post.Revision.ToString()) return Results.Conflict();
            post = null;
            return Results.NoContent();
        });
        await api.StartAsync(token);
        var address = api.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        await using var client = await McpClient.CreateAsync(CreateTransport(address, key), cancellationToken: token);
        var tools = await client.ListToolsAsync(cancellationToken: token);
        Assert.Equal(new[]
        {
            "create_publication", "delete_publication", "get_bug_report", "get_bug_report_screenshot", "get_publication",
            "list_bug_reports", "list_publications", "list_suggestions", "publish_publication", "unpublish_publication",
            "update_bug_report", "update_publication"
        }, tools.Select(tool => tool.Name).Order().ToArray());
        foreach (var tool in tools.Where(tool => tool.Name.Contains("publication", StringComparison.Ordinal)))
        {
            var annotations = Assert.IsType<ToolAnnotations>(tool.ProtocolTool.Annotations);
            var read = tool.Name is "list_publications" or "get_publication";
            if (read) Assert.True(annotations.ReadOnlyHint);
            else Assert.NotEqual(true, annotations.ReadOnlyHint);
            if (tool.Name == "create_publication") Assert.NotEqual(true, annotations.IdempotentHint);
            else Assert.True(annotations.IdempotentHint);
            Assert.NotEqual(true, annotations.OpenWorldHint);
            if (tool.Name == "create_publication") Assert.NotEqual(true, annotations.DestructiveHint);
            else if (!read) Assert.True(annotations.DestructiveHint);
        }

        var draft = new Dictionary<string, object?>
        {
            ["kind"] = kind, ["language"] = "fr", ["title"] = "Nouvelles aventures",
            ["body"] = "Un monde à explorer.\nDeuxième paragraphe."
        };
        if (version is not null) draft["gameVersion"] = version;
        var created = Success(await client.CallToolAsync("create_publication", draft, cancellationToken: token));
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal(kind, created.GetProperty("kind").GetString());
        if (version is null) AssertOptionalNull(created, "gameVersion");
        else Assert.Equal(version, created.GetProperty("gameVersion").GetString());
        AssertOptionalNull(created, "publishedAt");
        AssertOptionalNull(created, "publishedRevision");
        Assert.Equal(1, created.GetProperty("revision").GetInt64());
        Assert.Single(requests);

        var list = Success(await client.CallToolAsync("list_publications", new Dictionary<string, object?>
        {
            ["kind"] = kind, ["language"] = "fr", ["offset"] = 20, ["limit"] = 10
        }, cancellationToken: token));
        Assert.True(list.GetProperty("hasMore").GetBoolean());
        Assert.Equal(id, list.GetProperty("items")[0].GetProperty("id").GetGuid());
        var detail = Success(await client.CallToolAsync("get_publication", new Dictionary<string, object?> { ["id"] = id }, cancellationToken: token));
        Assert.Equal("Nouvelles aventures", detail.GetProperty("title").GetString());

        var revision = new Dictionary<string, object?> { ["id"] = id, ["expectedRevision"] = 1 };
        var published = Success(await client.CallToolAsync("publish_publication", revision, cancellationToken: token));
        Assert.Equal(2, published.GetProperty("publishedRevision").GetInt64());
        var publishedAt = published.GetProperty("publishedAt").GetDateTimeOffset();
        var update = new Dictionary<string, object?>(draft) { ["id"] = id, ["expectedRevision"] = 2, ["title"] = "Brouillon suivant" };
        var saved = Success(await client.CallToolAsync("update_publication", update, cancellationToken: token));
        Assert.Equal(3, saved.GetProperty("revision").GetInt64());
        Assert.Equal(2, saved.GetProperty("publishedRevision").GetInt64());
        Assert.Equal(publishedAt, saved.GetProperty("publishedAt").GetDateTimeOffset());

        foreach (var name in new[] { "update_publication", "publish_publication", "unpublish_publication", "delete_publication" })
        {
            var arguments = name == "update_publication" ? update : new() { ["id"] = id, ["expectedRevision"] = 2 };
            var conflict = await client.CallToolAsync(name, arguments, cancellationToken: token);
            Assert.True(conflict.IsError);
            Assert.Contains("Read it again", Assert.Single(conflict.Content.OfType<TextContentBlock>()).Text);
            Assert.Equal(3, post!.Revision);
        }
        var unpublished = Success(await client.CallToolAsync("unpublish_publication", new Dictionary<string, object?>
        {
            ["id"] = id, ["expectedRevision"] = 3
        }, cancellationToken: token));
        Assert.Equal(4, unpublished.GetProperty("revision").GetInt64());
        AssertOptionalNull(unpublished, "publishedAt");
        AssertOptionalNull(unpublished, "publishedRevision");
        Assert.Equal("Brouillon suivant", unpublished.GetProperty("title").GetString());
        var deleted = await client.CallToolAsync("delete_publication", new Dictionary<string, object?>
        {
            ["id"] = id, ["expectedRevision"] = 4
        }, cancellationToken: token);
        Assert.NotEqual(true, deleted.IsError);
        Assert.Null(post);
        Assert.Equal(11, requests.Count);

        var requestCount = requests.Count;
        foreach (var invalid in InvalidArguments(id))
        {
            var result = await client.CallToolAsync(invalid.Tool, invalid.Arguments, cancellationToken: token);
            Assert.True(result.IsError);
        }
        Assert.Equal(requestCount, requests.Count);
        await api.StopAsync(token);
    }

    private static IEnumerable<(string Tool, Dictionary<string, object?> Arguments)> InvalidArguments(Guid id)
    {
        yield return ("get_publication", new() { ["id"] = Guid.Empty });
        yield return ("list_publications", new() { ["kind"] = "other" });
        yield return ("list_publications", new() { ["language"] = "other" });
        yield return ("list_publications", new() { ["offset"] = 10001 });
        yield return ("list_publications", new() { ["limit"] = 51 });
        yield return ("publish_publication", new() { ["id"] = id, ["expectedRevision"] = 0 });
        yield return ("delete_publication", new() { ["id"] = id, ["expectedRevision"] = long.MaxValue });
        yield return ("create_publication", new() { ["kind"] = "release-note", ["language"] = "fr", ["title"] = "Titre", ["body"] = "Texte" });
        yield return ("create_publication", new() { ["kind"] = "news", ["language"] = "fr", ["title"] = "Titre", ["body"] = "Texte", ["gameVersion"] = "1.0" });
        yield return ("create_publication", new() { ["kind"] = "news", ["language"] = "fr", ["title"] = "Titre\ninterdit", ["body"] = "Texte" });
    }

    private static StdioClientTransport CreateTransport(string address, string key) => new(new StdioClientTransportOptions
    {
        Name = "arkya-publications-protocol-test", Command = "dotnet", Arguments = [typeof(Program).Assembly.Location],
        EnvironmentVariables = new Dictionary<string, string?>
        {
            ["Reports__BaseUrl"] = address, ["Reports__AdminKey"] = new string('r', 40), ["Reports__AllowLoopbackHttp"] = "true",
            ["Publications__Enabled"] = "true", ["Publications__BaseUrl"] = address,
            ["Publications__AdminKey"] = key, ["Publications__AllowLoopbackHttp"] = "true"
        }
    });

    private static JsonElement Success(CallToolResult result)
    {
        Assert.NotEqual(true, result.IsError);
        using var json = JsonDocument.Parse(Assert.Single(result.Content.OfType<TextContentBlock>()).Text);
        return json.RootElement.Clone();
    }

    private static void AssertOptionalNull(JsonElement response, string property)
    {
        if (response.TryGetProperty(property, out var value)) Assert.Equal(JsonValueKind.Null, value.ValueKind);
    }

    private static StoredPost ReadDraft(JsonElement fields, StoredPost post) => post with
    {
        Kind = fields.GetProperty("kind").GetString()!, Language = fields.GetProperty("language").GetString()!,
        Title = fields.GetProperty("title").GetString()!, Body = fields.GetProperty("body").GetString()!,
        Summary = fields.TryGetProperty("summary", out var summary) ? summary.GetString() ?? "" : "",
        GameVersion = fields.TryGetProperty("gameVersion", out var version) ? version.GetString() : null
    };

    private sealed record StoredPost(Guid Id, long Revision, string Kind, string Language, string Title, string Summary,
        string Body, string? GameVersion, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
        DateTimeOffset? PublishedAt, long? PublishedRevision);
}
