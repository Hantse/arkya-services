using System.Net;
using System.Net.Http.Json;
using System.Text;
using Arkya.Content.Api.Application;
using Arkya.Content.Api.Contracts;
using Arkya.Content.Api.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arkya.Content.Tests;

public sealed class PostEndpointTests
{
    public static CreatePostRequest News(string title = "Actualité") => new("news", "fr", title, "Résumé", "Texte public\nDeuxième ligne", null);
    public static UpdatePostRequest Edit(AdminPostResponse post, string title = "Draft secret") =>
        new(post.Revision, post.Kind, post.Language, title, post.Summary, post.Body, post.GameVersion);

    [Fact]
    public async Task AdminCapabilityIsRequiredForEveryOperationAndPlayerAndServiceCredentialsDoNotGrantIt()
    {
        await using var factory = new ContentFactory();
        using var admin = factory.Client(ContentFactory.AdminKey);
        var created = (await (await admin.PostAsJsonAsync("/admin/content/posts", News())).Content.ReadFromJsonAsync<AdminPostResponse>())!;
        var path = $"/admin/content/posts/{created.Id}";
        foreach (var client in new[] { factory.Client(), factory.Client(serviceKey: ContentFactory.ServiceKey), factory.Client(ContentFactory.ServiceKey) })
        {
            using (client)
            {
                client.DefaultRequestHeaders.Authorization = new("Bearer", "player-session");
                Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/admin/content/posts")).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/admin/content/posts", News())).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync(path, Edit(created))).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(path + "/publish", new PostRevisionRequest(1))).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(path + "/unpublish", new PostRevisionRequest(1))).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync(path + "?revision=1")).StatusCode);
            }
        }
        admin.DefaultRequestHeaders.Add("X-Arkya-Admin-Key", ContentFactory.AdminKey);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task DraftEditsRemainPrivateUntilPublishedAndStaleCommandsCannotOverwriteOrWithdrawNewContent()
    {
        await using var factory = new ContentFactory();
        using var admin = factory.Client(ContentFactory.AdminKey);
        using var reader = factory.Client();
        using var create = await admin.PostAsJsonAsync("/admin/content/posts", News());
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var initial = (await create.Content.ReadFromJsonAsync<AdminPostResponse>())!;
        var path = $"/admin/content/posts/{initial.Id}";
        var publicPath = $"/v1/content/posts/{initial.Id}";
        Assert.Equal(1, initial.Revision);
        Assert.Null(initial.PublishedAt);
        Assert.Equal(HttpStatusCode.NotFound, (await reader.GetAsync(publicPath)).StatusCode);
        Assert.Empty((await reader.GetFromJsonAsync<PostPage<PublicPostResponse>>("/v1/content/posts"))!.Items);

        var published = (await (await admin.PostAsJsonAsync(path + "/publish", new PostRevisionRequest(1))).Content.ReadFromJsonAsync<AdminPostResponse>())!;
        Assert.Equal(2, published.Revision);
        Assert.Equal(published.Revision, published.PublishedRevision);
        Assert.Equal(FixedTimeProvider.Now, published.PublishedAt);
        var originalJson = await reader.GetStringAsync(publicPath);
        Assert.DoesNotContain("revision", originalJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("createdAt", originalJson);

        var edit = Edit(published) with { Kind = "release-note", Language = "en", GameVersion = "2026.10.23-beta+1" };
        var draft = (await (await admin.PutAsJsonAsync(path, edit)).Content.ReadFromJsonAsync<AdminPostResponse>())!;
        Assert.Equal(3, draft.Revision);
        Assert.Equal(2, draft.PublishedRevision);
        Assert.Equal(originalJson, await reader.GetStringAsync(publicPath));
        Assert.Single((await reader.GetFromJsonAsync<PostPage<PublicPostResponse>>("/v1/content/posts?kind=news&language=fr"))!.Items);
        Assert.Empty((await reader.GetFromJsonAsync<PostPage<PublicPostResponse>>("/v1/content/posts?kind=release-note&language=en"))!.Items);
        foreach (var operation in new[] { "publish", "unpublish" })
            Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync(path + "/" + operation, new PostRevisionRequest(2))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync(path, edit)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync(path + "?revision=2")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync(path + "/publish", new PostRevisionRequest(3))).StatusCode);
        var release = (await reader.GetFromJsonAsync<PublicPostResponse>(publicPath))!;
        Assert.Equal("Draft secret", release.Title);
        Assert.Equal("2026.10.23-beta+1", release.GameVersion);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync(path + "/unpublish", new PostRevisionRequest(4))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await reader.GetAsync(publicPath)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync(path + "?revision=5")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task FeedSupportsBoundedPaginationAndExactFiltersWithoutInventedContent()
    {
        await using var factory = new ContentFactory();
        using var admin = factory.Client(ContentFactory.AdminKey);
        using var reader = factory.Client();
        for (var i = 0; i < 3; i++)
        {
            var post = (await (await admin.PostAsJsonAsync("/admin/content/posts", News($"Article {i}"))).Content.ReadFromJsonAsync<AdminPostResponse>())!;
            await admin.PostAsJsonAsync($"/admin/content/posts/{post.Id}/publish", new PostRevisionRequest(1));
        }
        var first = (await reader.GetFromJsonAsync<PostPage<PublicPostResponse>>("/v1/content/posts?language=fr&limit=2"))!;
        var second = (await reader.GetFromJsonAsync<PostPage<PublicPostResponse>>("/v1/content/posts?language=fr&limit=2&offset=2"))!;
        Assert.Equal(2, first.Items.Count);
        Assert.True(first.HasMore);
        Assert.Single(second.Items);
        Assert.False(second.HasMore);
        Assert.Equal(3, first.Items.Concat(second.Items).Select(post => post.Id).Distinct().Count());
        Assert.Empty((await admin.GetFromJsonAsync<PostPage<AdminPostResponse>>("/admin/content/posts?kind=release-note"))!.Items);
    }

    [Theory]
    [InlineData("kind=other")]
    [InlineData("language=de")]
    [InlineData("limit=0")]
    [InlineData("limit=51")]
    [InlineData("offset=-1")]
    [InlineData("offset=10001")]
    [InlineData("limit=bad")]
    public async Task RejectsInvalidQueries(string query)
    {
        await using var factory = new ContentFactory();
        using var admin = factory.Client(ContentFactory.AdminKey);
        foreach (var path in new[] { "/admin/content/posts", "/v1/content/posts" })
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync(path + "?" + query)).StatusCode);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"kind\":\"news\",\"language\":\"fr\",\"title\":\"x\",\"body\":\"x\",\"publishedAt\":\"2026-01-01\"}")]
    [InlineData("{\"kind\":\"release-note\",\"language\":\"fr\",\"title\":\"x\",\"body\":\"x\"}")]
    [InlineData("{\"kind\":\"news\",\"language\":\"fr\",\"title\":\"x\",\"body\":\"x\",\"gameVersion\":\"1\"}")]
    public async Task RejectsInvalidPayloadAndServerOwnedFields(string json)
    {
        await using var factory = new ContentFactory();
        using var admin = factory.Client(ContentFactory.AdminKey);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/admin/content/posts", new StringContent(json, Encoding.UTF8, "application/json"))).StatusCode);
        Assert.Empty((await admin.GetFromJsonAsync<PostPage<AdminPostResponse>>("/admin/content/posts"))!.Items);
    }

    [Fact]
    public async Task TextLimitsAndControlCharactersAreValidatedWhilePlainTextIsPreserved()
    {
        await using var factory = new ContentFactory();
        using var admin = factory.Client(ContentFactory.AdminKey);
        var bad = new[] { News() with { Title = new string('x', 161) }, News() with { Title = "x\n" },
            News() with { Summary = new string('x', 401) }, News() with { Summary = "\t" },
            News() with { Body = new string('x', 20001) }, News() with { Body = "x\0" }, News() with { Body = " " },
            News() with { Kind = "release-note", GameVersion = new string('x', 65) },
            News() with { Kind = "release-note", GameVersion = "1<script>" } };
        foreach (var request in bad) Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/admin/content/posts", request)).StatusCode);
        var valid = News() with { Title = new string('x', 160), Summary = null, Body = "<script>literal text</script>\n\tSecond line" };
        var saved = (await (await admin.PostAsJsonAsync("/admin/content/posts", valid)).Content.ReadFromJsonAsync<AdminPostResponse>())!;
        Assert.Equal(valid.Body, saved.Body);
        Assert.Equal("", saved.Summary);
        Assert.Null(saved.GameVersion);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/admin/content/posts/{saved.Id}/publish", new PostRevisionRequest(null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/admin/content/posts/{saved.Id}", Edit(saved) with { Revision = long.MaxValue })).StatusCode);
    }

    [Fact]
    public async Task PublicCorsAllowsReadOnlyAnonymousRequestsAndNeverCredentialsOrAdminAccess()
    {
        await using var factory = new ContentFactory();
        using var client = factory.Client();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/v1/content/posts");
        request.Headers.Add("Origin", "https://arkya.online");
        request.Headers.Add("Access-Control-Request-Method", "GET");
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("*", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Equal("GET", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Methods")));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsJsonAsync("/v1/content/posts", News())).StatusCode);
        Assert.False((await client.GetAsync("/admin/content/posts")).Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task PublicPersistenceFailureReturnsSafe503InsteadOfAnInventedEmptyFeed()
    {
        await using var factory = new ContentFactory();
        await using var offline = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IPostRepository>();
            services.AddSingleton<IPostRepository, UnavailablePosts>();
        }));
        using var client = offline.CreateClient();
        var response = await client.GetAsync("/v1/content/posts");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain("private-database", await response.Content.ReadAsStringAsync());
    }

    private sealed class UnavailablePosts : IPostRepository
    {
        public Task<IReadOnlyList<ContentPost>> ListAsync(bool publishedOnly, string? kind, string? language, int limit, int offset,
            CancellationToken ct, PostFeedFilter? filter = null) => throw new TimeoutException("private-database");
        public Task CreateAsync(ContentPost post, CancellationToken ct) => throw new NotSupportedException();
        public Task<ContentPost?> FindAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> TryReplaceAsync(long expectedRevision, ContentPost replacement, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> TryDeleteAsync(Guid id, long expectedRevision, CancellationToken ct) => throw new NotSupportedException();
    }
}
