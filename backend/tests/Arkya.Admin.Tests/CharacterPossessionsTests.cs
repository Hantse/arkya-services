using System.Net;
using Arkya.Admin.Accounts;
using Arkya.ServiceDefaults.Characters;
using Microsoft.Extensions.Options;

namespace Arkya.Admin.Tests;

public sealed class CharacterPossessionsTests
{
    private static string Path(string section) => $"/Character/{AccountsStub.CharacterId:D}?section={section}";

    internal static AdminCharacterDetail SavedPossessions() => AccountsStub.SavedCharacter() with
    {
        Bags = new("available") { SlotCapacity = 150, Items = [new("0", "resource:wood", 83), new("7", "painted-sword", 1)] },
        Bank = new("available") { SlotCapacity = 150, Items = [new("12", "resource:enchanted-crystal", 40)] },
        Equipment = new("available")
        {
            Items = [new("main-hand", "painted-sword", 1), new("chest", "linen-tunic", 1)],
            HarvestTool = new("wood", 3, 250, 150, 2, [new(1250, 333)])
        }
    };

    [Theory]
    [InlineData("bags", "83", "Wood", "painted-sword", "2 / 150 slots occupied", "84 item(s)")]
    [InlineData("bank", "40", "Enchanted crystal", "resource:enchanted-crystal", "1 / 150 slots occupied", "40 item(s)")]
    [InlineData("equipment", "Main hand", "painted-sword", "linen-tunic", "Chest", "Harvest tool")]
    public async Task PersistedContentsShowActualItemsSlotsAndQuantities(string section, params string[] expected)
    {
        await using var factory = new AccountsFactory();
        factory.Upstream.CharacterDetail = SavedPossessions();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = WebUtility.HtmlDecode(await browser.GetStringAsync(Path(section)));
        foreach (var value in expected) Assert.Contains(value, html);
        Assert.DoesNotContain("Unavailable", html);
        Assert.DoesNotContain("not yet persisted", html);
        Assert.DoesNotContain("role=\"alert\"", html);
        Assert.Equal(HttpMethod.Get, Assert.Single(factory.Upstream.Calls).Method);
        AdminFactory.NoSecrets(html);
    }

    [Fact]
    public async Task PersistedStorageSlotsUseReadableOneBasedNumbersAndKeepGaps()
    {
        await using var factory = new AccountsFactory();
        factory.Upstream.CharacterDetail = SavedPossessions();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var bags = await browser.GetStringAsync(Path("bags"));
        Assert.Contains("<th scope=\"row\">1</th>", bags);
        Assert.Contains("<th scope=\"row\">8</th>", bags);
        Assert.DoesNotContain("<th scope=\"row\">2</th>", bags);
    }

    [Fact]
    public async Task HarvestToolAndSocketValuesShowTheirSavedBasisPointsPrecisely()
    {
        await using var factory = new AccountsFactory();
        factory.Upstream.CharacterDetail = SavedPossessions();
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync(Path("equipment"));
        foreach (var value in new[] { "Wood", "Level", "2.5%", "1.5%", "12.5%", "3.33%", "1 / 2 sockets occupied" })
            Assert.Contains(value, html);
        Assert.DoesNotContain("No harvest tool", html);
    }

    [Theory]
    [InlineData("bags", "Bags are empty")]
    [InlineData("bank", "Bank is empty")]
    [InlineData("equipment", "No equipped items")]
    public async Task ASavedEmptyContainerIsExplicitlyEmpty(string section, string expected)
    {
        await using var factory = new AccountsFactory();
        factory.Upstream.CharacterDetail = SavedPossessions() with
        {
            Bags = new("available") { SlotCapacity = 150 },
            Bank = new("available") { SlotCapacity = 150 }, Equipment = new("available")
        };
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync(Path(section));
        Assert.Contains(expected, html);
        Assert.DoesNotContain("Unavailable", html);
        Assert.DoesNotContain("role=\"alert\"", html);
        if (section == "equipment") Assert.Contains("No harvest tool is equipped", html);
        else Assert.Contains("0 / 150 slots occupied", html);
    }

    [Theory]
    [InlineData("bags", "not_saved")]
    [InlineData("bank", "not_saved")]
    [InlineData("equipment", "not_saved")]
    [InlineData("bags", "unsupported")]
    [InlineData("bank", "invalid")]
    [InlineData("equipment", "not_persisted")]
    public async Task MissingOrUnreadablePossessionsNeverInventEmptyContents(string section, string status)
    {
        await using var factory = new AccountsFactory();
        factory.Upstream.CharacterDetail = AccountsStub.SavedCharacter() with
        {
            Bags = new(status), Bank = new(status), Equipment = new(status)
        };
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync(Path(section));
        Assert.Contains("Unavailable", html);
        Assert.Contains("Unavailable data does not mean", html);
        Assert.DoesNotContain("slots occupied", html);
        Assert.DoesNotContain("No equipped items", html);
        Assert.DoesNotContain("are empty", html);
        Assert.DoesNotContain("role=\"alert\"", html);
    }

    [Fact]
    public async Task AHarvestToolWithoutCatalogEquipmentStillRemainsVisible()
    {
        await using var factory = new AccountsFactory();
        var detail = SavedPossessions();
        factory.Upstream.CharacterDetail = detail with { Equipment = detail.Equipment with { Items = [] } };
        using var browser = factory.Browser();
        await AdminFactory.Login(browser);
        var html = await browser.GetStringAsync(Path("equipment"));
        Assert.Contains("No equipped items", html);
        Assert.Contains("Harvest tool", html);
        Assert.Contains("12.5%", html);
        Assert.DoesNotContain("No harvest tool", html);
    }

    [Theory]
    [MemberData(nameof(InvalidPossessions))]
    public async Task MalformedPossessionsCannotBecomePlausibleSavedContents(AdminCharacterDetail detail)
    {
        using var upstream = new AccountsStub { CharacterDetail = detail };
        using var http = new HttpClient(upstream) { BaseAddress = new("https://characters.example/") };
        var client = new CharactersAdminClient(http,
            Options.Create(new CharactersAdminApiOptions { Enabled = true, AdminKey = AccountsStub.Key }));
        var error = await Assert.ThrowsAsync<PlayerAdminApiException>(() => client.GetAsync(AccountsStub.CharacterId, default));
        Assert.Equal(HttpStatusCode.BadGateway, error.StatusCode);
        Assert.DoesNotContain(AccountsStub.Key, error.Message);
    }

    public static IEnumerable<object[]> InvalidPossessions()
    {
        var detail = SavedPossessions();
        yield return [detail with { Bags = null! }];
        yield return [detail with { Bank = detail.Bank with { Items = null! } }];
        yield return [detail with { Bags = detail.Bags with { Status = "invented" } }];
        yield return [detail with { Bags = detail.Bags with { SlotCapacity = null } }];
        yield return [detail with { Bank = detail.Bank with { SlotCapacity = 151 } }];
        yield return [detail with { Bank = detail.Bank with { SlotCapacity = 0 } }];
        yield return [detail with { Bags = detail.Bags with { Items = [new("150", "resource:wood", 1)] } }];
        yield return [detail with { Bags = detail.Bags with { Items = [new("-1", "resource:wood", 1)] } }];
        yield return [detail with { Bags = detail.Bags with { Items = [new("01", "resource:wood", 1)] } }];
        yield return [detail with { Bags = detail.Bags with { Items = [new("0", "resource:wood", 0)] } }];
        yield return [detail with { Bags = detail.Bags with { Items = [new("0", "resource:wood", 1000001)] } }];
        yield return [detail with { Bags = detail.Bags with { Items = [new("0", "resource:wood", 1), new("0", "resource:stone", 1)] } }];
        yield return [detail with { Bags = detail.Bags with { Items = [new("0", "resource:unknown", 1)] } }];
        yield return [detail with { Bags = detail.Bags with { Items = [new("0", "<script>item</script>", 1)] } }];
        yield return [detail with { Bags = detail.Bags with { HarvestTool = detail.Equipment.HarvestTool } }];
        yield return [detail with { Bank = new("not_saved") { SlotCapacity = 150 } }];
        yield return [detail with { Bank = new("invalid") { Items = [new("0", "resource:wood", 1)] } }];
        yield return [detail with { Equipment = detail.Equipment with { SlotCapacity = 150 } }];
        yield return [detail with { Equipment = detail.Equipment with { Items = [new("none", "painted-sword", 1)] } }];
        yield return [detail with { Equipment = detail.Equipment with { Items = [new("head", "painted-sword", 2)] } }];
        yield return [detail with { Equipment = detail.Equipment with { Items = [new("head", "painted-sword", 1), new("head", "linen-tunic", 1)] } }];
        yield return [detail with { Equipment = detail.Equipment with { HarvestTool = detail.Equipment.HarvestTool! with { Family = "invalid" } } }];
        yield return [detail with { Equipment = detail.Equipment with { HarvestTool = detail.Equipment.HarvestTool! with { Level = 101 } } }];
        yield return [detail with { Equipment = detail.Equipment with { HarvestTool = detail.Equipment.HarvestTool! with { Level = 0 } } }];
        yield return [detail with { Equipment = detail.Equipment with { HarvestTool = detail.Equipment.HarvestTool! with { SocketCapacity = 0 } } }];
        yield return [detail with { Equipment = detail.Equipment with { HarvestTool = detail.Equipment.HarvestTool! with { Stones = null! } } }];
        yield return [detail with { Equipment = detail.Equipment with { HarvestTool = detail.Equipment.HarvestTool! with { Stones = [new(0, 10001)] } } }];
    }
}
