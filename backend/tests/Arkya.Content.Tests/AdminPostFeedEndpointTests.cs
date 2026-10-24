using System.Net;
using System.Net.Http.Json;
using Arkya.Content.Api.Application;
using Arkya.Content.Api.Contracts;
using Arkya.Content.Api.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Arkya.Content.Tests;

public sealed class AdminPostFeedEndpointTests
{
    [Fact]
    public async Task FiltersUseDraftAndUpdatedTimeBeforePagingIncludingUnpublishedPosts()
    {
        await using var factory = new ContentFactory();
        using var admin = factory.Client(ContentFactory.AdminKey);
        await AdminPostFeedFixtures.SeedAsync(factory.Services.GetRequiredService<IPostRepository>());
        const string query = "kind=release-note&language=fr&q=RECRUTEMENT&gameVersion=0.4-beta&from=2026-10-23&to=2026-10-23&limit=2";
        var first = (await admin.GetFromJsonAsync<PostPage<AdminPostResponse>>("/admin/content/posts?" + query))!;
        var second = (await admin.GetFromJsonAsync<PostPage<AdminPostResponse>>("/admin/content/posts?" + query + "&offset=2"))!;
        var third = (await admin.GetFromJsonAsync<PostPage<AdminPostResponse>>("/admin/content/posts?" + query + "&offset=4"))!;
        Assert.Equal(new[] { AdminPostFeedFixtures.Id(3), AdminPostFeedFixtures.Id(10) }, first.Items.Select(post => post.Id));
        Assert.Equal(new[] { AdminPostFeedFixtures.Id(9), AdminPostFeedFixtures.Id(2) }, second.Items.Select(post => post.Id));
        Assert.Equal(AdminPostFeedFixtures.Id(1), Assert.Single(third.Items).Id);
        Assert.True(first.HasMore);
        Assert.True(second.HasMore);
        Assert.False(third.HasMore);
        Assert.Null(second.Items[0].PublishedAt);
        var oldest = (await admin.GetFromJsonAsync<PostPage<AdminPostResponse>>("/admin/content/posts?" + query + "&sort=oldest"))!;
        Assert.Equal(new[] { AdminPostFeedFixtures.Id(1), AdminPostFeedFixtures.Id(2) }, oldest.Items.Select(post => post.Id));
        Assert.True(oldest.HasMore);
    }

    [Theory]
    [InlineData("TitleMarker", 1)]
    [InlineData("SUMMARYMARKER", 2)]
    [InlineData("bodymarker", 3)]
    [InlineData("literal.*[text]", 10)]
    [InlineData("PrivateOnlyMarker", 9)]
    public async Task SearchIsLiteralAndCaseInsensitiveAcrossDraftFields(string query, int expected)
    {
        await using var factory = new ContentFactory();
        using var admin = factory.Client(ContentFactory.AdminKey);
        await AdminPostFeedFixtures.SeedAsync(factory.Services.GetRequiredService<IPostRepository>());
        var page = (await admin.GetFromJsonAsync<PostPage<AdminPostResponse>>("/admin/content/posts?q=" + Uri.EscapeDataString(query)))!;
        Assert.Equal(AdminPostFeedFixtures.Id(expected), Assert.Single(page.Items).Id);
        Assert.False(page.HasMore);
        Assert.Empty((await admin.GetFromJsonAsync<PostPage<AdminPostResponse>>("/admin/content/posts?q=PublishedOnlyMarker"))!.Items);
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
    public async Task InvalidAdminFiltersReturnBadRequest(string query)
    {
        await using var factory = new ContentFactory();
        using var admin = factory.Client(ContentFactory.AdminKey);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/admin/content/posts?" + query)).StatusCode);
    }

    [Fact]
    public async Task SearchAndVersionBoundsAreEnforcedAndCalendarExtremesDoNotOverflow()
    {
        await using var factory = new ContentFactory();
        using var admin = factory.Client(ContentFactory.AdminKey);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/admin/content/posts?q=" + new string('a', 101))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/admin/content/posts?gameVersion=" + new string('a', 65))).StatusCode);
        var valid = "/admin/content/posts?from=0001-01-01&to=9999-12-31&q=" + new string('a', 100) + "&gameVersion=" + new string('a', 64);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(valid)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/admin/content/posts?from=2024-02-29&to=2024-02-29")).StatusCode);
    }

    [Fact]
    public async Task CalendarRangeUsesWholeUtcUpdatedDayAndVersionComparisonIsExact()
    {
        await using var factory = new ContentFactory();
        using var admin = factory.Client(ContentFactory.AdminKey);
        await AdminPostFeedFixtures.SeedAsync(factory.Services.GetRequiredService<IPostRepository>());
        var page = (await admin.GetFromJsonAsync<PostPage<AdminPostResponse>>(
            "/admin/content/posts?from=2026-10-23&to=2026-10-23&sort=oldest"))!;
        Assert.Equal(9, page.Items.Count);
        Assert.All(page.Items, post => Assert.Equal(new DateOnly(2026, 10, 23), DateOnly.FromDateTime(post.UpdatedAt.UtcDateTime)));
        Assert.DoesNotContain(page.Items, post => post.Id == AdminPostFeedFixtures.Id(4) || post.Id == AdminPostFeedFixtures.Id(5));
        Assert.Empty((await admin.GetFromJsonAsync<PostPage<AdminPostResponse>>("/admin/content/posts?gameVersion=0.4-BETA"))!.Items);
        Assert.Equal(12, (await admin.GetFromJsonAsync<PostPage<AdminPostResponse>>("/admin/content/posts?q=%20%20"))!.Items.Count);
    }
}

internal static class AdminPostFeedFixtures
{
    public static Guid Id(int index) => Guid.Parse($"10000000-0000-0000-0000-{index:000000000000}");

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
        await Add(9, start.AddHours(12), "Recrutement PrivateOnlyMarker", published: false);
        await Add(10, start.AddHours(15), "Recrutement literal.*[text]");
        await Add(11, start.AddHours(16), "Unrelated draft", kind: "news", language: "en", version: null, matchingSnapshot: true);
        await Add(12, start.AddDays(1), "Recrutement changed tomorrow", matchingSnapshot: true);

        async Task Add(int id, DateTimeOffset updatedAt, string title, string summary = "", string body = "Texte privé",
            string kind = "release-note", string language = "fr", string? version = "0.4-beta", bool published = true,
            bool matchingSnapshot = false)
        {
            var draft = new PostContent(kind, language, title, summary, body, version);
            var snapshot = matchingSnapshot
                ? new PostContent("release-note", "fr", "Recrutement PublishedOnlyMarker", "", "Recrutement", "0.4-beta")
                : new PostContent("news", "en", "PublishedOnlyMarker", "", "Public text", null);
            await repository.CreateAsync(new(Id(id), 3, draft, start.AddDays(-10), updatedAt,
                published ? new(2, start.AddHours(1), snapshot) : null), default);
        }
    }
}
