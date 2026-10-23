using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Arkya.Content.Api.Application;
using Arkya.Content.Api.Contracts;

namespace Arkya.Content.Tests;

public sealed class ItemDropSourceTests
{
    public static IEnumerable<object[]> InvalidSources()
    {
        yield return [new ItemDropSourceRequest?[] { new("unregistered-mob", 2500) }, "dropSources[0].mobId"];
        yield return [new ItemDropSourceRequest?[] { new("Prototype-Monster", 2500) }, "dropSources[0].mobId"];
        yield return [new ItemDropSourceRequest?[] { new(null, 2500) }, "dropSources[0].mobId"];
        yield return [new ItemDropSourceRequest?[] { null }, "dropSources[0]"];
        yield return [new ItemDropSourceRequest?[] { new(ContentService.SupportedEnemyId, 0) }, "dropSources[0].chanceBasisPoints"];
        yield return [new ItemDropSourceRequest?[] { new(ContentService.SupportedEnemyId, -1) }, "dropSources[0].chanceBasisPoints"];
        yield return [new ItemDropSourceRequest?[] { new(ContentService.SupportedEnemyId, 10001) }, "dropSources[0].chanceBasisPoints"];
        yield return [new ItemDropSourceRequest?[] { new(ContentService.SupportedEnemyId, 1000), new(ContentService.SupportedEnemyId, 2000) }, "dropSources[1].mobId"];
        yield return [Enumerable.Repeat<ItemDropSourceRequest?>(new(ContentService.SupportedEnemyId, 100), 33).ToArray(), "dropSources"];
    }

    [Theory]
    [MemberData(nameof(InvalidSources))]
    public async Task InvalidSourcesCannotChangeDraft(ItemDropSourceRequest?[] sources, string expectedField)
    {
        var repository = new MemoryItemContentRepository();
        var service = new ItemContentService(repository, new FixedTimeProvider());
        await repository.UpsertAssetAsync(ItemFixtures.Asset(), CancellationToken.None);
        var result = await service.SaveDraftAsync(new(0, [ItemFixtures.Request() with { DropSources = sources }]), CancellationToken.None);
        Assert.Equal(ItemChangeStatus.Invalid, result.Status);
        Assert.Contains($"items[0].{expectedField}", result.Errors!.Keys);
        Assert.Equal(0, (await repository.GetDraftAsync(CancellationToken.None)).Revision);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1250)]
    [InlineData(10000)]
    public async Task ExactChancesAreSavedAndFrozenInPublishedSnapshots(int chanceBasisPoints)
    {
        await using var factory = new ContentFactory();
        using var admin = factory.Client(adminKey: ContentFactory.AdminKey);
        using var server = factory.Client(serviceKey: ContentFactory.ServiceKey);
        await admin.PutAsJsonAsync($"/admin/content/item-assets/{ItemFixtures.Guid}", ItemFixtures.AssetRequest());
        var item = ItemFixtures.Request() with
        {
            DropSources = [new(ContentService.SupportedEnemyId, chanceBasisPoints)]
        };
        var save = await admin.PutAsJsonAsync("/admin/content/items/draft", new SaveItemDraftRequest(0, [item]));
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        var saved = (await save.Content.ReadFromJsonAsync<ItemDraftResponse>())!;
        Assert.Equal(chanceBasisPoints, Assert.Single(Assert.Single(saved.Items).DropSources).ChanceBasisPoints);
        var publication = await admin.PostAsJsonAsync("/admin/content/items/catalogs", new PublishItemCatalogRequest(1));
        Assert.Equal(HttpStatusCode.OK, publication.StatusCode);
        var original = await publication.Content.ReadAsStringAsync();
        var catalog = (await publication.Content.ReadFromJsonAsync<ItemCatalogResponse>())!;
        var drop = Assert.Single(Assert.Single(catalog.Items).DropSources);
        Assert.Equal(ContentService.SupportedEnemyId, drop.MobId);
        Assert.Equal(chanceBasisPoints, drop.ChanceBasisPoints);

        var remove = await admin.PutAsJsonAsync("/admin/content/items/draft",
            new SaveItemDraftRequest(1, [item with { DropSources = [] }]));
        Assert.Equal(HttpStatusCode.OK, remove.StatusCode);
        Assert.Empty(Assert.Single((await remove.Content.ReadFromJsonAsync<ItemDraftResponse>())!.Items).DropSources);
        var nextPublication = await admin.PostAsJsonAsync("/admin/content/items/catalogs", new PublishItemCatalogRequest(2));
        Assert.Empty(Assert.Single((await nextPublication.Content.ReadFromJsonAsync<ItemCatalogResponse>())!.Items).DropSources);
        Assert.Equal(original, await server.GetStringAsync($"/internal/content/items/catalogs/{catalog.Version:D}"));
        Assert.Equal(original, await (await admin.PostAsJsonAsync("/admin/content/items/catalogs", new PublishItemCatalogRequest(1))).Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HistoricalItemsWithMissingOrNullDropSourcesRemainEditable(bool explicitNull)
    {
        await using var factory = new ContentFactory();
        using var admin = factory.Client(adminKey: ContentFactory.AdminKey);
        await admin.PutAsJsonAsync($"/admin/content/item-assets/{ItemFixtures.Guid}", ItemFixtures.AssetRequest());
        var item = JsonSerializer.SerializeToNode(ItemFixtures.Request(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        if (!explicitNull) item.AsObject().Remove("dropSources");
        var json = "{\"revision\":0,\"items\":[" + item.ToJsonString() + "]}";
        var response = await admin.PutAsync("/admin/content/items/draft", new StringContent(json, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(Assert.Single((await response.Content.ReadFromJsonAsync<ItemDraftResponse>())!.Items).DropSources);
    }

    [Fact]
    public async Task MobChoicesShareTheSupportedGameplayIdentityAndRequireAdminAuthentication()
    {
        await using var factory = new ContentFactory();
        using var admin = factory.Client(adminKey: ContentFactory.AdminKey);
        var choices = (await admin.GetFromJsonAsync<ItemMobResponse[]>("/admin/content/item-mobs"))!;
        var mob = Assert.Single(choices);
        Assert.Equal(ContentService.SupportedEnemyId, mob.MobId);
        Assert.Equal("Prototype monster", mob.Name);
        using var anonymous = factory.Client();
        using var server = factory.Client(serviceKey: ContentFactory.ServiceKey);
        using var wrongAdmin = factory.Client(adminKey: ContentFactory.ServiceKey);
        foreach (var client in new[] { anonymous, server, wrongAdmin })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/admin/content/item-mobs")).StatusCode);
        admin.DefaultRequestHeaders.Add("X-Arkya-Admin-Key", ContentFactory.AdminKey);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/admin/content/item-mobs")).StatusCode);
    }
}
