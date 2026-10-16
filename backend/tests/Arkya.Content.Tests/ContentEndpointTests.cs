using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Arkya.Content.Api.Contracts;
using Arkya.Content.Api.Application;
using Arkya.Content.Api.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arkya.Content.Tests;

public sealed class ContentEndpointTests
{
    [Fact]
    public async Task AdminAndDedicatedCapabilitiesAreDistinctFromPlayerSessions()
    {
        await using var factory = new ContentFactory();
        using var anonymous = factory.Client();
        using var player = factory.Client();
        player.DefaultRequestHeaders.Authorization = new("Bearer", "arkya-player-session");
        using var server = factory.Client(serviceKey: ContentFactory.ServiceKey);
        using var wrongAdmin = factory.Client(adminKey: ContentFactory.ServiceKey);
        using var wrongService = factory.Client(serviceKey: ContentFactory.AdminKey);
        using var admin = factory.Client(adminKey: ContentFactory.AdminKey);

        foreach (var client in new[] { anonymous, player, server, wrongAdmin })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/admin/content/draft")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/admin/content/catalogs")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync("/admin/content/draft", new SaveDraftRequest(0, []))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/admin/content/catalogs", new PublishCatalogRequest(0))).StatusCode);
        }
        var route = $"/internal/content/catalogs/{Guid.NewGuid():D}";
        foreach (var client in new[] { anonymous, player, admin, wrongService })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await server.GetAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/health")).StatusCode);
    }

    [Fact]
    public async Task PublicationIsAnImmutableVersionedSnapshotAndRetryRemainsIdempotentAfterEditing()
    {
        await using var factory = new ContentFactory();
        using var admin = factory.Client(adminKey: ContentFactory.AdminKey);
        using var server = factory.Client(serviceKey: ContentFactory.ServiceKey);

        var initial = (await admin.GetFromJsonAsync<DraftResponse>("/admin/content/draft"))!;
        Assert.Equal(0, initial.Revision);
        Assert.Empty(initial.Quests);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync("/admin/content/catalogs", new PublishCatalogRequest(0))).StatusCode);
        var saved = await admin.PutAsJsonAsync("/admin/content/draft", new SaveDraftRequest(0, [ContentFixtures.Quest()]));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(1, (await saved.Content.ReadFromJsonAsync<DraftResponse>())!.Revision);

        var published = await admin.PostAsJsonAsync("/admin/content/catalogs", new PublishCatalogRequest(1));
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        var originalJson = await published.Content.ReadAsStringAsync();
        var catalog = JsonSerializer.Deserialize<CatalogResponse>(originalJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(1, catalog.SchemaVersion);
        Assert.NotEqual(Guid.Empty, catalog.Version);
        Assert.Equal(FixedTimeProvider.Now, catalog.PublishedAt);
        Assert.Equal(3, Assert.Single(catalog.Quests).RequiredKills);
        var route = $"/internal/content/catalogs/{catalog.Version:D}";
        Assert.Equal(originalJson, await server.GetStringAsync(route));

        var edited = await admin.PutAsJsonAsync("/admin/content/draft", new SaveDraftRequest(1, [ContentFixtures.Quest(8)]));
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var retry = await admin.PostAsJsonAsync("/admin/content/catalogs", new PublishCatalogRequest(1));
        Assert.Equal(originalJson, await retry.Content.ReadAsStringAsync());
        Assert.Equal(originalJson, await server.GetStringAsync(route));
        var second = await admin.PostAsJsonAsync("/admin/content/catalogs", new PublishCatalogRequest(2));
        var secondCatalog = (await second.Content.ReadFromJsonAsync<CatalogResponse>())!;
        Assert.NotEqual(catalog.Version, secondCatalog.Version);
        Assert.Equal(8, Assert.Single(secondCatalog.Quests).RequiredKills);
        var history = (await admin.GetFromJsonAsync<CatalogResponse[]>("/admin/content/catalogs"))!;
        Assert.Equal(new[] { secondCatalog.Version, catalog.Version }, history.Select(item => item.Version));
        Assert.Equal(originalJson, await server.GetStringAsync(route));
    }

    [Fact]
    public async Task ConcurrentEditorsHaveOneWinnerAndStaleUnpublishedRevisionsCannotPublish()
    {
        await using var factory = new ContentFactory();
        using var admin = factory.Client(adminKey: ContentFactory.AdminKey);

        var saves = await Task.WhenAll(
            admin.PutAsJsonAsync("/admin/content/draft", new SaveDraftRequest(0, [ContentFixtures.Quest(3)])),
            admin.PutAsJsonAsync("/admin/content/draft", new SaveDraftRequest(0, [ContentFixtures.Quest(7)])));
        Assert.Single(saves, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(saves, response => response.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsJsonAsync("/admin/content/catalogs", new PublishCatalogRequest(0))).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await admin.PutAsJsonAsync("/admin/content/draft", new SaveDraftRequest(1, [ContentFixtures.Quest(9)]))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsJsonAsync("/admin/content/catalogs", new PublishCatalogRequest(1))).StatusCode);
        Assert.Empty((await admin.GetFromJsonAsync<CatalogResponse[]>("/admin/content/catalogs"))!);
        foreach (var response in saves)
            response.Dispose();
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"revision\":0,\"quests\":null}")]
    [InlineData("{\"revision\":0,\"quests\":[null]}")]
    [InlineData("{\"revision\":0,\"quests\":[],\"publishedAt\":\"2026-01-01\"}")]
    [InlineData("{\"revision\":-1,\"quests\":[]}")]
    public async Task RejectsMissingInvalidAndCallerControlledFields(string json)
    {
        await using var factory = new ContentFactory();
        using var admin = factory.Client(adminKey: ContentFactory.AdminKey);
        var response = await admin.PutAsync("/admin/content/draft", new StringContent(json, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, (await admin.GetFromJsonAsync<DraftResponse>("/admin/content/draft"))!.Revision);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"draftRevision\":null}")]
    [InlineData("{\"draftRevision\":-1}")]
    [InlineData("{\"draftRevision\":0,\"version\":\"custom\"}")]
    public async Task PublicationRequiresExplicitRevisionAndRefusesCallerVersion(string json)
    {
        await using var factory = new ContentFactory();
        using var admin = factory.Client(adminKey: ContentFactory.AdminKey);
        var response = await admin.PostAsync("/admin/content/catalogs", new StringContent(json, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RejectsOversizedPayloadAndDuplicateCredentials()
    {
        await using var factory = new ContentFactory();
        using var admin = factory.Client(adminKey: ContentFactory.AdminKey);
        using var oversized = await admin.PutAsync("/admin/content/draft", new StringContent(new string(' ', 524289), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
        admin.DefaultRequestHeaders.Add("X-Arkya-Admin-Key", ContentFactory.AdminKey);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/admin/content/draft")).StatusCode);
    }

    [Fact]
    public async Task PersistenceFailureReturns503WithoutExposingInfrastructureOrCredentials()
    {
        await using var factory = new ContentFactory();
        await using var offline = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IContentRepository>();
            services.AddSingleton<IContentRepository, UnavailableRepository>();
        }));
        using var admin = offline.CreateClient();
        admin.DefaultRequestHeaders.Add("X-Arkya-Admin-Key", ContentFactory.AdminKey);
        using var response = await admin.GetAsync("/admin/content/draft");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(ContentFactory.AdminKey, body);
        Assert.DoesNotContain("database.example", body);
    }

    private sealed class UnavailableRepository : IContentRepository
    {
        public Task<QuestDraft> GetDraftAsync(CancellationToken cancellationToken) =>
            throw new TimeoutException($"database.example rejected {ContentFactory.AdminKey}");
        public Task<QuestDraft?> TrySaveDraftAsync(long expectedRevision, IReadOnlyList<QuestDefinition> quests,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PublicationResult> PublishAsync(long expectedRevision, DateTimeOffset publishedAt,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PublishedCatalog?> FindCatalogAsync(Guid version, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<PublishedCatalog>> ListCatalogsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
