using System.Net;
using System.Net.Http.Json;
using System.Text;
using Arkya.Content.Api.Contracts;

namespace Arkya.Content.Tests;

public sealed class ItemContentEndpointTests
{
    [Fact]
    public async Task ItemPublicationFreezesDefinitionsAndVisualReferencesAndRetriesSurviveRegistryRemoval()
    {
        await using var factory = new ContentFactory();
        using var admin = factory.Client(adminKey: ContentFactory.AdminKey);
        using var server = factory.Client(serviceKey: ContentFactory.ServiceKey);
        Assert.Equal(HttpStatusCode.NotFound, (await server.GetAsync("/internal/content/items/catalogs/latest")).StatusCode);
        var initial = (await admin.GetFromJsonAsync<ItemDraftResponse>("/admin/content/items/draft"))!;
        Assert.Equal(0, initial.Revision); Assert.Empty(initial.Items);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/admin/content/items/catalogs", new PublishItemCatalogRequest(0))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/admin/content/items/draft", new SaveItemDraftRequest(0, [ItemFixtures.Request()]))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/admin/content/item-assets/{ItemFixtures.Guid}", ItemFixtures.AssetRequest())).StatusCode);
        var saved = await admin.PutAsJsonAsync("/admin/content/items/draft", new SaveItemDraftRequest(0, [ItemFixtures.Request()]));
        Assert.Equal(1, (await saved.Content.ReadFromJsonAsync<ItemDraftResponse>())!.Revision);
        var published = await admin.PostAsJsonAsync("/admin/content/items/catalogs", new PublishItemCatalogRequest(1));
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        var original = await published.Content.ReadAsStringAsync();
        var catalog = (await published.Content.ReadFromJsonAsync<ItemCatalogResponse>())!;
        Assert.Equal(1, catalog.SchemaVersion); Assert.NotEqual(Guid.Empty, catalog.Version);
        var item = Assert.Single(catalog.Items);
        Assert.Equal(ItemFixtures.Hash, item.VisualDependencyHash);
        Assert.Equal("Assets/Arkya/Items/Sword.prefab", item.VisualAssetPath);
        Assert.Equal(5, item.Attack);
        Assert.Equal(original, await server.GetStringAsync("/internal/content/items/catalogs/latest"));
        var route = $"/internal/content/items/catalogs/{catalog.Version:D}";
        Assert.Equal(original, await server.GetStringAsync(route));

        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/admin/content/item-assets/{ItemFixtures.Guid}",
            ItemFixtures.AssetRequest("Assets/Arkya/Items/SwordV2.prefab", new string('b', 32)))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/admin/content/items/draft", new SaveItemDraftRequest(1, [ItemFixtures.Request(attack: 17)]))).StatusCode);
        var second = await admin.PostAsJsonAsync("/admin/content/items/catalogs", new PublishItemCatalogRequest(2));
        var next = (await second.Content.ReadFromJsonAsync<ItemCatalogResponse>())!;
        Assert.NotEqual(catalog.Version, next.Version);
        Assert.Equal(17, Assert.Single(next.Items).Attack);
        Assert.Equal("Assets/Arkya/Items/SwordV2.prefab", Assert.Single(next.Items).VisualAssetPath);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/admin/content/item-assets/{ItemFixtures.Guid}")).StatusCode);
        var retry = await admin.PostAsJsonAsync("/admin/content/items/catalogs", new PublishItemCatalogRequest(1));
        Assert.Equal(original, await retry.Content.ReadAsStringAsync());
        Assert.Equal(original, await server.GetStringAsync(route));
        var history = (await admin.GetFromJsonAsync<ItemCatalogResponse[]>("/admin/content/items/catalogs"))!;
        Assert.Equal(new[] { next.Version, catalog.Version }, history.Select(entry => entry.Version));
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/admin/content/items/draft", new SaveItemDraftRequest(2, []))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/admin/content/items/catalogs", new PublishItemCatalogRequest(3))).StatusCode);
    }

    [Fact]
    public async Task ConcurrentSavesHaveOneWinnerAndStaleDraftCannotPublish()
    {
        await using var factory = new ContentFactory();
        using var admin = factory.Client(adminKey: ContentFactory.AdminKey);
        await admin.PutAsJsonAsync($"/admin/content/item-assets/{ItemFixtures.Guid}", ItemFixtures.AssetRequest());
        var saves = await Task.WhenAll(
            admin.PutAsJsonAsync("/admin/content/items/draft", new SaveItemDraftRequest(0, [ItemFixtures.Request(attack: 5)])),
            admin.PutAsJsonAsync("/admin/content/items/draft", new SaveItemDraftRequest(0, [ItemFixtures.Request(attack: 17)])));
        Assert.Single(saves, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(saves, response => response.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/admin/content/items/draft", new SaveItemDraftRequest(1, [ItemFixtures.Request()]))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/admin/content/items/catalogs", new PublishItemCatalogRequest(1))).StatusCode);
        await admin.DeleteAsync($"/admin/content/item-assets/{ItemFixtures.Guid}");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/admin/content/items/catalogs", new PublishItemCatalogRequest(2))).StatusCode);
        foreach (var response in saves) response.Dispose();
    }

    [Fact]
    public async Task AssetGallerySupportsBoundedLiteralSearchAndSafePngWithoutBinaryMetadataInJson()
    {
        await using var factory = new ContentFactory();
        using var admin = factory.Client(adminKey: ContentFactory.AdminKey);
        var metadataPng = ItemFixtures.Png(addMetadata: true);
        var upload = await admin.PutAsJsonAsync($"/admin/content/item-assets/{ItemFixtures.Guid}", ItemFixtures.AssetRequest() with
        { Name = "Sword [A]", ThumbnailPngBase64 = Convert.ToBase64String(metadataPng) });
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        var metadata = (await upload.Content.ReadFromJsonAsync<ItemAssetResponse>())!;
        Assert.True(metadata.HasThumbnail);
        var json = await upload.Content.ReadAsStringAsync();
        Assert.DoesNotContain("thumbnailPngBase64", json); Assert.DoesNotContain("should be removed", json);
        var page = (await admin.GetFromJsonAsync<ItemAssetPage>("/admin/content/item-assets?search=%5BA%5D&limit=1"))!;
        Assert.Equal(ItemFixtures.Guid, Assert.Single(page.Assets).Guid); Assert.False(page.HasMore);
        Assert.Empty((await admin.GetFromJsonAsync<ItemAssetPage>("/admin/content/item-assets?search=.*"))!.Assets);
        var thumbnail = await admin.GetAsync($"/admin/content/item-assets/{ItemFixtures.Guid}/thumbnail");
        Assert.Equal("image/png", thumbnail.Content.Headers.ContentType!.MediaType);
        Assert.Equal("nosniff", Assert.Single(thumbnail.Headers.GetValues("X-Content-Type-Options")));
        var bytes = await thumbnail.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length < metadataPng.Length); Assert.DoesNotContain("should be removed", Encoding.ASCII.GetString(bytes));
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/admin/content/item-assets?limit=51")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/admin/content/item-assets?offset=-1")).StatusCode);
    }

    [Fact]
    public async Task PlayerAndServerCredentialsCannotManageItemsAndAdminCannotReadInternalCatalogs()
    {
        await using var factory = new ContentFactory();
        using var anonymous = factory.Client();
        using var server = factory.Client(serviceKey: ContentFactory.ServiceKey);
        using var wrongAdmin = factory.Client(adminKey: ContentFactory.ServiceKey);
        using var admin = factory.Client(adminKey: ContentFactory.AdminKey);
        foreach (var client in new[] { anonymous, server, wrongAdmin })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/admin/content/items/draft")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/admin/content/items/catalogs")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/admin/content/item-assets")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/admin/content/item-assets/{ItemFixtures.Guid}/thumbnail")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync("/admin/content/items/draft", new SaveItemDraftRequest(0, []))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/admin/content/items/catalogs", new PublishItemCatalogRequest(0))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/admin/content/item-assets/{ItemFixtures.Guid}", ItemFixtures.AssetRequest())).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/admin/content/item-assets/{ItemFixtures.Guid}")).StatusCode);
        }
        foreach (var client in new[] { anonymous, admin })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/internal/content/items/catalogs/latest")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/internal/content/items/catalogs/{Guid.NewGuid():D}")).StatusCode);
        }
        admin.DefaultRequestHeaders.Add("X-Arkya-Admin-Key", ContentFactory.AdminKey);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/admin/content/item-assets")).StatusCode);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"revision\":0,\"items\":[null]}")]
    [InlineData("{\"revision\":0,\"items\":[],\"version\":\"caller-controlled\"}")]
    [InlineData("{\"revision\":-1,\"items\":[]}")]
    public async Task DraftRejectsMissingInvalidOrCallerControlledFields(string json)
    {
        await using var factory = new ContentFactory();
        using var admin = factory.Client(adminKey: ContentFactory.AdminKey);
        var response = await admin.PutAsync("/admin/content/items/draft", new StringContent(json, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, (await admin.GetFromJsonAsync<ItemDraftResponse>("/admin/content/items/draft"))!.Revision);
    }
}
