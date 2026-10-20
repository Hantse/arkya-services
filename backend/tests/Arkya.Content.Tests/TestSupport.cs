using Arkya.Content.Api.Application;
using Arkya.Content.Api.Contracts;
using Arkya.Content.Api.Domain;
using Arkya.Content.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arkya.Content.Tests;

internal sealed class FixedTimeProvider : TimeProvider
{
    public static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}

internal sealed class MemoryContentRepository : IContentRepository
{
    private QuestDraft draft = new(0, []);
    private readonly Dictionary<long, PublishedCatalog> catalogs = [];
    private readonly object gate = new();

    public Task<QuestDraft> GetDraftAsync(CancellationToken cancellationToken)
    {
        lock (gate)
            return Task.FromResult(draft with { Quests = draft.Quests.ToArray() });
    }

    public Task<QuestDraft?> TrySaveDraftAsync(long expectedRevision, IReadOnlyList<QuestDefinition> quests,
        CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (expectedRevision != draft.Revision)
                return Task.FromResult<QuestDraft?>(null);
            draft = new(draft.Revision + 1, quests.ToArray());
            return Task.FromResult<QuestDraft?>(draft with { Quests = draft.Quests.ToArray() });
        }
    }

    public Task<PublicationResult> PublishAsync(long expectedRevision, DateTimeOffset publishedAt, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (catalogs.TryGetValue(expectedRevision, out var published))
                return Task.FromResult(new PublicationResult(PublicationStatus.Published, Copy(published)));
            if (draft.Revision != expectedRevision || draft.Quests.Count == 0)
                return Task.FromResult(new PublicationResult(PublicationStatus.Conflict));
            published = new(1, Guid.NewGuid(), publishedAt, draft.Quests.ToArray());
            catalogs.Add(expectedRevision, published);
            return Task.FromResult(new PublicationResult(PublicationStatus.Published, Copy(published)));
        }
    }

    public Task<PublishedCatalog?> FindCatalogAsync(Guid version, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            var catalog = catalogs.Values.SingleOrDefault(catalog => catalog.Version == version);
            return Task.FromResult(catalog is null ? null : Copy(catalog));
        }
    }

    public Task<IReadOnlyList<PublishedCatalog>> ListCatalogsAsync(CancellationToken cancellationToken)
    {
        lock (gate)
            return Task.FromResult<IReadOnlyList<PublishedCatalog>>(catalogs.OrderByDescending(pair => pair.Key)
                .Select(pair => Copy(pair.Value)).ToArray());
    }

    private static PublishedCatalog Copy(PublishedCatalog catalog) => catalog with { Quests = catalog.Quests.ToArray() };
}

internal sealed class ContentFactory(bool production = false) : WebApplicationFactory<Arkya.Content.Api.Program>
{
    public const string AdminKey = "content-administrator-test-key-123456789";
    public const string ServiceKey = "content-dedicated-server-test-key-123456";
    public Dictionary<string, string?> Settings { get; } = [];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(production ? "Production" : "Development");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ContentAdmin:Key"] = AdminKey,
                ["ServiceAuthentication:Key"] = ServiceKey,
                ["Mongo:ConnectionString"] = "mongodb://localhost:27017",
                ["Mongo:DatabaseName"] = "arkya_content_tests"
            });
            configuration.AddInMemoryCollection(Settings);
        });
        builder.ConfigureServices(services =>
        {
            services.Remove(services.Single(descriptor => descriptor.ImplementationType == typeof(ContentStoreInitializer)));
            services.RemoveAll<IContentRepository>();
            services.AddSingleton<IContentRepository, MemoryContentRepository>();
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider, FixedTimeProvider>();
        });
    }

    public HttpClient Client(string? adminKey = null, string? serviceKey = null)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        if (adminKey is not null)
            client.DefaultRequestHeaders.Add("X-Arkya-Admin-Key", adminKey);
        if (serviceKey is not null)
            client.DefaultRequestHeaders.Add("X-Arkya-Service-Key", serviceKey);
        return client;
    }
}

internal static class ContentFixtures
{
    public static QuestRequest Quest(int kills = 3, string questId = "clear-the-path") =>
        new(questId, "Dégager le chemin", "Éliminer les ennemis proches du passage.", "prototype-monster", kills);

    public static QuestDefinition Definition(int kills = 3) =>
        new("clear-the-path", "Dégager le chemin", "Éliminer les ennemis proches du passage.", "prototype-monster", kills);
}
