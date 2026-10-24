using System.Net;
using System.Net.Http.Json;
using Arkya.Content.Api.Application;
using Arkya.Content.Api.Contracts;
using Arkya.Content.Api.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Arkya.Content.Tests;

public sealed class PostFeedEndpointTests
{
    [Fact]
    public async Task AllFiltersUsePublishedSnapshotBeforePagingAndStableSorting()
    {
        await using var factory = new ContentFactory();
        using var reader = factory.Client();
        await PostFeedFixtures.SeedAsync(factory.Services.GetRequiredService<IPostRepository>());
        const string query = "kind=release-note&language=fr&q=RECRUTEMENT&gameVersion=0.4-beta&from=2026-10-23&to=2026-10-23&limit=2";
        var first = (await reader.GetFromJsonAsync<PostPage<PublicPostResponse>>("/v1/content/posts?" + query))!;
        var second = (await reader.GetFromJsonAsync<PostPage<PublicPostResponse>>("/v1/content/posts?" + query + "&offset=2"))!;
        var empty = (await reader.GetFromJsonAsync<PostPage<PublicPostResponse>>("/v1/content/posts?" + query + "&offset=4"))!;
        Assert.Equal(new[] { PostFeedFixtures.Id(3), PostFeedFixtures.Id(10) }, first.Items.Select(post => post.Id));
        Assert.True(first.HasMore);
        Assert.Equal(new[] { PostFeedFixtures.Id(2), PostFeedFixtures.Id(1) }, second.Items.Select(post => post.Id));
        Assert.False(second.HasMore);
        Assert.Empty(empty.Items);
        Assert.False(empty.HasMore);
        Assert.All(first.Items.Concat(second.Items), post =>
        {
            Assert.Equal("release-note", post.Kind);
            Assert.Equal("fr", post.Language);
            Assert.Equal("0.4-beta", post.GameVersion);
            Assert.DoesNotContain("private draft", post.Title);
        });

        var ascending = (await reader.GetFromJsonAsync<PostPage<PublicPostResponse>>("/v1/content/posts?" + query + "&sort=oldest"))!;
        Assert.Equal(new[] { PostFeedFixtures.Id(1), PostFeedFixtures.Id(2) }, ascending.Items.Select(post => post.Id));
        Assert.True(ascending.HasMore);
        var explicitlyNewest = (await reader.GetFromJsonAsync<PostPage<PublicPostResponse>>("/v1/content/posts?" + query + "&sort=newest"))!;
        Assert.Equal(first.Items, explicitlyNewest.Items);

        var privateSearch = (await reader.GetFromJsonAsync<PostPage<PublicPostResponse>>("/v1/content/posts?q=private%20draft"))!;
        Assert.Empty(privateSearch.Items);
        Assert.False(privateSearch.HasMore);
        Assert.Empty((await reader.GetFromJsonAsync<PostPage<PublicPostResponse>>("/v1/content/posts?gameVersion=9.9-private"))!.Items);
    }

    [Theory]
    [InlineData("TitleMarker", 1)]
    [InlineData("SUMMARYMARKER", 2)]
    [InlineData("bodymarker", 3)]
    [InlineData("literal.*[text]", 10)]
    public async Task SearchIsCaseInsensitiveLiteralSubstringAcrossPublishedFields(string query, int expected)
    {
        await using var factory = new ContentFactory();
        using var reader = factory.Client();
        await PostFeedFixtures.SeedAsync(factory.Services.GetRequiredService<IPostRepository>());
        var page = (await reader.GetFromJsonAsync<PostPage<PublicPostResponse>>("/v1/content/posts?q=" + Uri.EscapeDataString(query)))!;
        Assert.Equal(PostFeedFixtures.Id(expected), Assert.Single(page.Items).Id);
        Assert.False(page.HasMore);
    }

    [Theory]
    [InlineData("q=x%0Ay")]
    [InlineData("q=x%00y")]
    [InlineData("gameVersion=")]
    [InlineData("gameVersion=%3Cscript%3E")]
    [InlineData("gameVersion=+1")]
    [InlineData("sort=")]
    [InlineData("sort=recent")]
    [InlineData("from=2026-02-29")]
    [InlineData("to=2026-10-32")]
    [InlineData("from=2026-1-01")]
    [InlineData("from=2026-10-23T00%3A00%3A00Z")]
    [InlineData("from=")]
    [InlineData("to=0000-01-01")]
    [InlineData("from=2026-10-24&to=2026-10-23")]
    public async Task InvalidPublicFiltersReturnBadRequest(string query)
    {
        await using var factory = new ContentFactory();
        using var reader = factory.Client();
        Assert.Equal(HttpStatusCode.BadRequest, (await reader.GetAsync("/v1/content/posts?" + query)).StatusCode);
    }

    [Fact]
    public async Task SearchAndVersionBoundsAreEnforcedAndCalendarExtremesDoNotOverflow()
    {
        await using var factory = new ContentFactory();
        using var reader = factory.Client();
        Assert.Equal(HttpStatusCode.BadRequest, (await reader.GetAsync("/v1/content/posts?q=" + new string('a', 101))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await reader.GetAsync("/v1/content/posts?gameVersion=" + new string('a', 65))).StatusCode);
        var valid = "/v1/content/posts?from=0001-01-01&to=9999-12-31&q=" + new string('a', 100) + "&gameVersion=" + new string('a', 64);
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync(valid)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync("/v1/content/posts?from=2024-02-29&to=2024-02-29")).StatusCode);
    }

    [Fact]
    public async Task CalendarRangeIncludesEntireUtcDayAndVersionComparisonIsExact()
    {
        await using var factory = new ContentFactory();
        using var reader = factory.Client();
        await PostFeedFixtures.SeedAsync(factory.Services.GetRequiredService<IPostRepository>());
        var page = (await reader.GetFromJsonAsync<PostPage<PublicPostResponse>>(
            "/v1/content/posts?from=2026-10-23&to=2026-10-23&sort=oldest"))!;
        Assert.Equal(8, page.Items.Count);
        Assert.All(page.Items, post => Assert.Equal(new DateOnly(2026, 10, 23), DateOnly.FromDateTime(post.PublishedAt.UtcDateTime)));
        Assert.DoesNotContain(page.Items, post => post.Id == PostFeedFixtures.Id(4) || post.Id == PostFeedFixtures.Id(5));
        Assert.Empty((await reader.GetFromJsonAsync<PostPage<PublicPostResponse>>("/v1/content/posts?gameVersion=0.4-BETA"))!.Items);
        var whitespace = (await reader.GetFromJsonAsync<PostPage<PublicPostResponse>>("/v1/content/posts?q=%20%20"))!;
        Assert.Equal(10, whitespace.Items.Count);
    }
}

internal static class PostFeedFixtures
{
    public static Guid Id(int index) => Guid.Parse($"00000000-0000-0000-0000-{index:000000000000}");
    public static async Task SeedAsync(IPostRepository repository)
    {
        var start = new DateTimeOffset(2026, 10, 23, 0, 0, 0, TimeSpan.Zero);
        await Add(1, start, "Recrutement TitleMarker");
        await Add(2, start, "Article", summary: "Recrutement SummaryMarker");
        await Add(3, start.AddDays(1).AddMilliseconds(-1), "Article", body: "Recrutement BodyMarker");
        await Add(4, start.AddDays(1), "Recrutement demain");
        await Add(5, start.AddMilliseconds(-1), "Recrutement hier");
        await Add(6, start.AddHours(12), "Recrutement English", language: "en");
        await Add(7, start.AddHours(12), "Recrutement autre version", version: "0.40-beta");
        await Add(8, start.AddHours(12), "Recrutement news", kind: "news", version: null);
        var draftOnly = new PostContent("release-note", "fr", "private draft recrutement", "", "private draft", "9.9-private");
        await repository.CreateAsync(new(Id(9), 1, draftOnly, start, start, null), default);
        await Add(10, start.AddHours(15), "Recrutement literal.*[text]", changedDraft: draftOnly);
        await Add(11, start.AddHours(16), "Sans correspondance", changedDraft: draftOnly);

        async Task Add(int id, DateTimeOffset date, string title, string summary = "", string body = "Texte public",
            string kind = "release-note", string language = "fr", string? version = "0.4-beta", PostContent? changedDraft = null)
        {
            var content = new PostContent(kind, language, title, summary, body, version);
            await repository.CreateAsync(new(Id(id), 3, changedDraft ?? content, start, start.AddDays(2), new(2, date, content)), default);
        }
    }
}
