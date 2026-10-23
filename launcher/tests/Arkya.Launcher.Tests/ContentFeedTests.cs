using System.Net;
using System.Text;
using System.Text.Json;
using Arkya.Launcher.Core;

namespace Arkya.Launcher.Tests;

public sealed class ContentFeedTests
{
    private const string Url = "https://api.arkya.online/v1/content/posts?language=fr&limit=12";
    private static ContentPost Post() => new("release-1", "release-note", "fr", "Une nouvelle version",
        "Résumé", "<script>text remains text</script>\nChangements.", "0.1.0-beta.20261023", DateTimeOffset.UtcNow);

    [Fact]
    public async Task ReadsPublishedNewsAndVersionNotesAsPlainTextWithoutCredentials()
    {
        HttpRequestMessage? request = null;
        var first = Post();
        using var http = new HttpClient(new Handler(message =>
        {
            request = message;
            return Response(new[] { first, first with { Id = "news-1", Kind = "news", GameVersion = null } });
        }));
        var posts = await new ContentFeedClient(http).GetPostsAsync(Url, CancellationToken.None);
        Assert.Equal(2, posts.Count);
        Assert.Equal(first.Body, posts[0].Body);
        Assert.Null(request!.Headers.Authorization);
        Assert.False(request.Headers.Contains("Cookie"));
    }

    [Fact]
    public async Task AcceptsAnEmptyFeed()
    {
        using var http = new HttpClient(new Handler(_ => Response([])));
        Assert.Empty(await new ContentFeedClient(http).GetPostsAsync(Url, CancellationToken.None));
    }

    [Theory]
    [InlineData("kind")]
    [InlineData("title")]
    [InlineData("body")]
    [InlineData("version")]
    [InlineData("date")]
    public async Task RejectsMalformedPublishedContent(string change)
    {
        var post = change switch
        {
            "kind" => Post() with { Kind = "draft" },
            "title" => Post() with { Title = new string('x', 161) },
            "body" => Post() with { Body = new string('x', 20001) },
            "version" => Post() with { GameVersion = null },
            _ => Post() with { PublishedAt = default }
        };
        using var http = new HttpClient(new Handler(_ => Response([post])));
        await Assert.ThrowsAsync<InvalidDataException>(() => new ContentFeedClient(http).GetPostsAsync(Url, CancellationToken.None));
    }

    [Fact]
    public async Task BoundsTheResponseBeforeDeserialization()
    {
        using var http = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(new string('x', 2 * 1024 * 1024 + 1)) }));
        await Assert.ThrowsAsync<InvalidDataException>(() => new ContentFeedClient(http).GetPostsAsync(Url, CancellationToken.None));
    }

    [Fact]
    public async Task ReportsServiceOutagesWithoutAnEmptySuccess()
    {
        using var http = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        await Assert.ThrowsAsync<HttpRequestException>(() => new ContentFeedClient(http).GetPostsAsync(Url, CancellationToken.None));
    }

    private static HttpResponseMessage Response(ContentPost[] items) => new(HttpStatusCode.OK)
    { Content = new StringContent(JsonSerializer.Serialize(new { items, hasMore = false }, GameManifest.JsonOptions), Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response(request));
    }
}
