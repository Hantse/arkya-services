using System.Net;
using Arkya.Admin.Content;

namespace Arkya.Admin.Tests;

public sealed class ItemLootEndpointTests
{
    private static Dictionary<string, string> Form(string token, string? chance = "12.5", string mob = "prototype-monster") => new()
    {
        ["__RequestVerificationToken"] = token, ["Revision"] = "3", ["Input.ItemId"] = "sword", ["Input.Name"] = "Lamedune",
        ["Input.Category"] = "weapon", ["Input.Rarity"] = "rare", ["Input.EquipmentSlot"] = "main-hand",
        ["Input.VisualAssetGuid"] = ContentStub.AssetGuid, ["Input.MaxStack"] = "1", ["Input.WeightGrams"] = "1200",
        ["Input.RequiredLevel"] = "2", ["Input.Attack"] = "12", ["Input.Defense"] = "0",
        ["Input.DropSources[0].MobId"] = mob, ["Input.DropSources[0].ChancePercent"] = chance ?? ""
    };

    [Theory]
    [InlineData("12.5", 1250)]
    [InlineData("12,34", 1234)]
    [InlineData("0.01", 1)]
    [InlineData("100", 10000)]
    public async Task SavesExactPercentageAndRestoresItInTheEditor(string percentage, int expected)
    {
        await using var factory = new AdminFactory();
        var other = ContentStub.Sword with { ItemId = "shield", DropSources = [new("prototype-monster", 25)] };
        factory.Content.ItemsDraft = new(3, [ContentStub.Sword, other]);
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = Form(await AdminFactory.Token(browser, "/Item/sword"), percentage);
        using var response = await browser.PostAsync("/Item/sword?handler=Save", AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var source = Assert.Single(factory.Content.ItemsDraft.Items[0].DropSources!);
        Assert.Equal(new ItemDropSource("prototype-monster", expected), source);
        Assert.Equal(other with { DropSources = null }, factory.Content.ItemsDraft.Items[1] with { DropSources = null });
        Assert.Equal(other.DropSources, factory.Content.ItemsDraft.Items[1].DropSources);
        var html = await browser.GetStringAsync("/Item/sword");
        Assert.Equal((expected / 100m).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
            AdminFactory.Input(html, "Input.DropSources[0].ChancePercent"));
        Assert.DoesNotContain(factory.Content.Calls, c => c.Method == HttpMethod.Post);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("100.01")]
    [InlineData("0.001")]
    [InlineData("-5")]
    [InlineData("NaN")]
    [InlineData("1e2")]
    [InlineData("")]
    public async Task InvalidChanceRetainsInputWithoutSaving(string chance)
    {
        await using var factory = new AdminFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = Form(await AdminFactory.Token(browser, "/Item/sword"), chance);
        using var response = await browser.PostAsync("/Item/sword?handler=Save", AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Enter a chance", html);
        Assert.Equal(chance, AdminFactory.Input(html, "Input.DropSources[0].ChancePercent"));
        Assert.DoesNotContain(factory.Content.Calls, c => c.Method != HttpMethod.Get);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectsUnknownOrDuplicateMobs(bool duplicate)
    {
        await using var factory = new AdminFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = Form(await AdminFactory.Token(browser, "/Item/sword"), "25", duplicate ? "prototype-monster" : "invented-mob");
        if (duplicate) { form["Input.DropSources[1].MobId"] = "prototype-monster"; form["Input.DropSources[1].ChancePercent"] = "50"; }
        using var response = await browser.PostAsync("/Item/sword?handler=Save", AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(duplicate ? "already linked" : "Choose a mob", await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain(factory.Content.Calls, c => c.Method != HttpMethod.Get);
    }

    [Fact]
    public async Task MobLibraryOutageRetainsChangesAndDoesNotWrite()
    {
        await using var factory = new AdminFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = Form(await AdminFactory.Token(browser, "/Item/sword"));
        factory.Content.ItemMobsError = HttpStatusCode.ServiceUnavailable;
        using var response = await browser.PostAsync("/Item/sword?handler=Save", AdminFactory.Form(form));
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Your changes are retained", html);
        Assert.Equal("12.5", AdminFactory.Input(html, "Input.DropSources[0].ChancePercent"));
        Assert.Contains("prototype-monster", html);
        Assert.DoesNotContain(factory.Content.Calls, c => c.Method != HttpMethod.Get);
    }

    [Fact]
    public async Task ChanceWithoutAMobIsRejected()
    {
        await using var factory = new AdminFactory();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = Form(await AdminFactory.Token(browser, "/Item/sword"), "25", "");
        using var response = await browser.PostAsync("/Item/sword?handler=Save", AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Choose a mob from the library", await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain(factory.Content.Calls, c => c.Method != HttpMethod.Get);
    }

    [Fact]
    public async Task RemovingAllSourcesPersistsAnEmptyArray()
    {
        await using var factory = new AdminFactory();
        factory.Content.ItemsDraft = new(3, [ContentStub.Sword with { DropSources = [new("prototype-monster", 500)] }]);
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var form = Form(await AdminFactory.Token(browser, "/Item/sword"), "", "");
        using var response = await browser.PostAsync("/Item/sword?handler=Save", AdminFactory.Form(form));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Empty(factory.Content.ItemsDraft.Items[0].DropSources!);
    }
}
