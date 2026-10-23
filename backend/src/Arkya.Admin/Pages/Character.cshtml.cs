using System.Net;
using System.Numerics;
using Arkya.Admin.Content;
using Arkya.Admin.Accounts;
using Arkya.ServiceDefaults;
using Arkya.ServiceDefaults.Characters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Arkya.Admin.Pages;

public sealed class CharacterModel(CharactersAdminClient characters) : PageModel
{
    public static readonly (string Id, string Label)[] Sections =
    [
        ("overview", "Overview"), ("stats", "Statistics"), ("quests", "Quest history"),
        ("bags", "Bags"), ("bank", "Bank"), ("equipment", "Equipment"), ("appearance", "Appearance")
    ];
    [BindProperty(SupportsGet = true)] public string? Section { get; set; }
    [BindProperty(SupportsGet = true)] public string? RealmId { get; set; }
    [BindProperty(SupportsGet = true)] public int QuestPage { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public int QuestPageSize { get; set; } = 25;
    public Guid Id { get; private set; }
    public AdminCharacterDetail? Detail { get; private set; }
    public string? Error { get; private set; }
    public bool HasNextQuests => Detail is not null && (long)QuestPage * QuestPageSize < Detail.Quests.Total;
    public static string ClassName(string? id) => id switch
    {
        "warrior" => "Warrior", "scout" => "Scout", "mage" => "Mage", "cleric" => "Cleric", "druid" => "Druid", _ => "—"
    };
    public static string Date(DateTimeOffset? value) => value?.UtcDateTime.ToString("dd/MM/yyyy HH:mm:ss") ?? "Not recorded";
    public static int RankCount(uint? ranks) => BitOperations.PopCount(ranks ?? 0);
    public static string Experience(AdminCharacterClassProgress progress) => progress.Level switch
    {
        0 => "Locked", 10 => "Level cap", _ => $"{progress.Experience} / {progress.Level * 100} XP"
    };
    public static string ItemName(string itemId) => itemId switch
    {
        "resource:wood" => "Wood", "resource:enchanted-wood" => "Enchanted wood",
        "resource:stone" => "Stone", "resource:enchanted-stone" => "Enchanted stone",
        "resource:ore" => "Ore", "resource:enchanted-ore" => "Enchanted ore",
        "resource:crystal" => "Crystal", "resource:enchanted-crystal" => "Enchanted crystal",
        "resource:berries" => "Berries", "resource:enchanted-berries" => "Enchanted berries",
        "resource:fiber" => "Fiber", "resource:enchanted-fiber" => "Enchanted fiber", _ => itemId
    };
    public static string StorageSlot(string slot) => (int.Parse(slot, System.Globalization.CultureInfo.InvariantCulture) + 1).ToString();
    public static string EquipmentSlot(string slot) => ContentItemPresentation.Slots.GetValueOrDefault(slot, slot);
    public static string ResourceFamily(string family) => family switch
    {
        "wood" => "Wood", "stone" => "Stone", "ore" => "Ore", "crystal" => "Crystal", "berries" => "Berries", "fiber" => "Fiber", _ => family
    };
    public static string Percent(int basisPoints) => (basisPoints / 100m).ToString("0.##") + "%";
    public static string PossessionsMessage(string status) => status switch
    {
        "unsupported" => "This saved possessions format is not supported by the administration console.",
        "invalid" => "The saved possessions could not be read. No replacement contents are shown.",
        "not_persisted" => "These possessions are not yet persisted by the connected dedicated server.",
        _ => "No possessions have been saved for this character yet. Enter the game with a compatible dedicated server and save the character to record them."
    };
    public string GameplayMessage => Detail?.Gameplay.Status switch
    {
        "unsupported" => "This saved statistics format is not supported by the administration console.",
        "invalid" => "The saved statistics could not be read. No replacement values are shown.",
        _ => "No statistics have been saved for this character yet. Enter the game with a compatible dedicated server and save the character to record them."
    };

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken ct)
    {
        Id = id;
        Section ??= "overview";
        if (!characters.Enabled || id == Guid.Empty) return NotFound();
        if (!ModelState.IsValid || !Sections.Any(section => section.Id == Section) ||
            RealmId is not null && !GameProtocol.ValidId(RealmId) ||
            QuestPage is < 1 or > 100000 || QuestPageSize is < 1 or > 100) return BadRequest();
        Response.Headers.CacheControl = "no-store";
        try { Detail = await characters.GetAsync(id, ct, RealmId, QuestPage, QuestPageSize); }
        catch (PlayerAdminApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { return NotFound(); }
        catch (Exception ex) when (PlayerAdminPresentation.IsDependencyError(ex, ct)) { Error = PlayerAdminPresentation.ErrorMessage(ex); }
        return Page();
    }
}
