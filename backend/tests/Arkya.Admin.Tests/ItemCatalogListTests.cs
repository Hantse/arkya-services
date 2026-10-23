using System.Net;
using System.Text.RegularExpressions;
using Arkya.Admin.Content;

namespace Arkya.Admin.Tests;

public sealed class ItemCatalogListTests
{
    private static ItemDefinition[] Items() =>
    [
        ContentStub.Sword with { ItemId = "bronze-sword", Name = "Bronze sword", RequiredLevel = 10,
            DropSources = [new("prototype-monster", 1250)] },
        ContentStub.Sword with { ItemId = "iron", Name = "Iron ore", Category = "material", Rarity = "common", EquipmentSlot = "none", RequiredLevel = 2 },
        ContentStub.Sword with { ItemId = "aegis", Name = "Aegis shield", Category = "armor", Rarity = "epic", EquipmentSlot = "off-hand", RequiredLevel = 25,
            DropSources = [new("other-monster", 10000)] }
    ];

    private static string[] Rows(string html) => Regex.Matches(html, "data-item-id=\"([^\"]+)\"")
        .Select(match => WebUtility.HtmlDecode(match.Groups[1].Value)).ToArray();

    [Theory]
    [InlineData("Search=%20SWORD%20", "bronze-sword")]
    [InlineData("Search=ORE", "iron")]
    [InlineData("Category=armor", "aegis")]
    [InlineData("Rarity=rare", "bronze-sword")]
    [InlineData("Drops=unlinked", "iron")]
    [InlineData("Drops=linked", "aegis,bronze-sword")]
    [InlineData("MobId=prototype-monster", "bronze-sword")]
    [InlineData("MobId=other-monster", "aegis")]
    [InlineData("MinLevel=10&MaxLevel=25", "aegis,bronze-sword")]
    [InlineData("Category=weapon&Rarity=rare&Drops=linked&MobId=prototype-monster&MinLevel=10&MaxLevel=10", "bronze-sword")]
    [InlineData("Category=armor&Rarity=common", "")]
    public async Task GetFiltersCombineWithoutMutatingTheDraft(string query, string expected)
    {
        await using var factory = new AdminFactory();
        var original = Items();
        factory.Content.ItemsDraft = new(3, original);
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        using var response = await browser.GetAsync("/Items?" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(expected.Length == 0 ? [] : expected.Split(','), Rows(html));
        Assert.Contains("matching", html);
        if (expected.Length == 0) Assert.Contains("No items match these filters", html);
        Assert.Same(original, factory.Content.ItemsDraft.Items);
        Assert.DoesNotContain(factory.Content.Calls, c => c.Method != HttpMethod.Get);
    }

    [Theory]
    [InlineData("name", "aegis,bronze-sword,iron")]
    [InlineData("name-desc", "iron,bronze-sword,aegis")]
    [InlineData("level", "iron,bronze-sword,aegis")]
    [InlineData("level-desc", "aegis,bronze-sword,iron")]
    [InlineData("rarity", "aegis,bronze-sword,iron")]
    public async Task SortingIsAppliedToMatchingItems(string sort, string expected)
    {
        await using var factory = new AdminFactory();
        factory.Content.ItemsDraft = new(3, Items());
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync("/Items?Sort=" + sort);
        Assert.Equal(expected.Split(','), Rows(html));
    }

    [Theory]
    [InlineData("MinLevel=0")]
    [InlineData("MaxLevel=1001")]
    [InlineData("MinLevel=ten")]
    [InlineData("MinLevel=20&MaxLevel=2")]
    [InlineData("PageNumber=0")]
    [InlineData("PageNumber=2147483648")]
    [InlineData("Category=unknown")]
    [InlineData("Rarity=unknown")]
    [InlineData("Drops=unknown")]
    [InlineData("Sort=unknown")]
    public async Task InvalidFiltersDoNotCallContent(string query)
    {
        await using var factory = new AdminFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        using var response = await browser.GetAsync("/Items?" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.Content.Calls);
    }

    [Fact]
    public async Task PaginationPreservesFiltersAndClampsOutOfRangePages()
    {
        await using var factory = new AdminFactory();
        factory.Content.ItemsDraft = new(3, Enumerable.Range(1, 50).Select(i => ContentStub.Sword with
        { ItemId = $"guard-{i:00}", Name = $"Guard {i:00}" }).ToArray());
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var first = await browser.GetStringAsync("/Items?Search=Guard&Category=weapon&Rarity=rare&Sort=name-desc");
        Assert.Equal(24, Rows(first).Length);
        Assert.Equal("guard-50", Rows(first)[0]);
        var next = WebUtility.HtmlDecode(Regex.Match(first, "href=\"([^\"]*PageNumber=2[^\"]*)\"").Groups[1].Value);
        Assert.Contains("Search=Guard", next);
        Assert.Contains("Category=weapon", next);
        Assert.Contains("Rarity=rare", next);
        Assert.Contains("Sort=name-desc", next);
        var second = await browser.GetStringAsync(next);
        Assert.Equal(24, Rows(second).Length);
        Assert.Equal("guard-26", Rows(second)[0]);
        Assert.Empty(Rows(first).Intersect(Rows(second)));
        var last = await browser.GetStringAsync("/Items?PageNumber=2147483647");
        Assert.Equal(["guard-49", "guard-50"], Rows(last));
        Assert.Contains("Page 3 of 3", last);
    }

    [Fact]
    public async Task DropPercentagesModelsAndEditLinksAreVisibleWithLegacyItems()
    {
        await using var factory = new AdminFactory();
        factory.Content.ItemsDraft = new(3, Items());
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        using var response = await browser.GetAsync("/Items");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.True(response.IsSuccessStatusCode, html);
        Assert.Contains("12.5%", html);
        Assert.Contains("100%", html);
        Assert.Contains("/Item/iron#drop-sources", html);
        Assert.Contains("handler=Thumbnail", html);
        Assert.Contains("Publish all saved items", html);
        Assert.Contains("item-catalog.css", html);
        AdminFactory.NoSecrets(html);
    }

    [Fact]
    public async Task SearchAndFallbackMobLabelsAreHtmlEncoded()
    {
        await using var factory = new AdminFactory();
        factory.Content.ItemsDraft = new(3, [ContentStub.Sword with { DropSources = [new("<script>mob</script>", 1)] }]);
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync("/Items?Search=%3Cscript%3Esearch%3C%2Fscript%3E");
        Assert.DoesNotContain("<script>search", html);
        Assert.DoesNotContain("<script>mob", html);
        Assert.Contains("&lt;script&gt;search", html);
        Assert.Contains("&lt;script&gt;mob", html);
    }

    [Fact]
    public async Task MobLookupFailureKeepsSavedItemsAndTheirDropRulesVisible()
    {
        await using var factory = new AdminFactory();
        factory.Content.ItemsDraft = new(3, Items());
        factory.Content.ItemMobsError = HttpStatusCode.ServiceUnavailable;
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync("/Items");
        Assert.Equal(["aegis", "bronze-sword", "iron"], Rows(html));
        Assert.Contains("Mob names are temporarily unavailable", html);
        Assert.Contains("prototype-monster", html);
        Assert.Contains("12.5%", html);
    }
}
