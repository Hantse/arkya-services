using System.Text.RegularExpressions;

namespace Arkya.ServiceDefaults.Characters;

public sealed record CharacterPossessionsSnapshot(int SchemaVersion, CharacterStorageSnapshot Bags,
    CharacterStorageSnapshot Bank, CharacterEquippedItem[] Equipment)
{
    public CharacterHarvestToolSnapshot? HarvestTool { get; init; }
}

public sealed record CharacterStorageSnapshot(int SlotCapacity, CharacterItemStack[] Slots);
public sealed record CharacterItemStack(int Slot, string ItemId, int Quantity);
public sealed record CharacterEquippedItem(string Slot, string ItemId, int Quantity);
public sealed record CharacterHarvestToolSnapshot(string Family, int Level, int BaseYieldBonusBps,
    int YieldBonusPerLevelBps, int SocketCapacity, CharacterSocketedStone[] Stones);
public sealed record CharacterSocketedStone(int YieldBonusBps, int ResourceWeightReductionBps);

public static partial class CharacterPossessionRules
{
    public const int SchemaVersion = 1;
    public const int MaximumStorageSlots = 150;
    public const int MaximumStackQuantity = 1_000_000;
    public const int EquipmentSlotCount = 12;

    public static bool IsValid(CharacterPossessionsSnapshot? value) => value is { SchemaVersion: SchemaVersion } &&
        IsValidStorage(value.Bags) && IsValidStorage(value.Bank) && value.Equipment is { Length: <= EquipmentSlotCount } &&
        value.Equipment.All(item => item is not null && IsEquipmentSlot(item.Slot) && IsItemId(item.ItemId) && item.Quantity == 1) &&
        value.Equipment.Select(item => item.Slot).Distinct(StringComparer.Ordinal).Count() == value.Equipment.Length &&
        (value.HarvestTool is null || IsValidHarvestTool(value.HarvestTool));

    public static bool IsValidStorage(CharacterStorageSnapshot? value) =>
        value is { SlotCapacity: >= 1 and <= MaximumStorageSlots, Slots: not null } && value.Slots.Length <= value.SlotCapacity &&
        value.Slots.All(item => item is not null && item.Slot >= 0 && item.Slot < value.SlotCapacity &&
            IsItemId(item.ItemId) && item.Quantity is >= 1 and <= MaximumStackQuantity) &&
        value.Slots.Select(item => item.Slot).Distinct().Count() == value.Slots.Length;

    public static bool IsValidHarvestTool(CharacterHarvestToolSnapshot? value) => value is
        { Level: >= 0 and <= 100, BaseYieldBonusBps: >= 0, YieldBonusPerLevelBps: >= 0, SocketCapacity: >= 0 and <= 6, Stones: not null } &&
        IsResourceFamily(value.Family) && value.Stones.Length <= value.SocketCapacity &&
        (value.Level > 0 || value.SocketCapacity == 0) && value.Stones.All(stone => stone is
            { YieldBonusBps: >= 0, ResourceWeightReductionBps: >= 0 and <= 10000 });

    public static bool IsResourceFamily(string? value) => value is "wood" or "stone" or "ore" or "crystal" or "berries" or "fiber";
    public static bool IsEquipmentSlot(string? value) => value is "main-hand" or "off-hand" or "head" or "chest" or
        "hands" or "legs" or "feet" or "neck" or "shoulders" or "back" or "waist" or "finger";

    public static bool IsItemId(string? value) => value is
        "resource:wood" or "resource:enchanted-wood" or "resource:stone" or "resource:enchanted-stone" or
        "resource:ore" or "resource:enchanted-ore" or "resource:crystal" or "resource:enchanted-crystal" or
        "resource:berries" or "resource:enchanted-berries" or "resource:fiber" or "resource:enchanted-fiber" ||
        value is "resource:sandstone" or "resource:stone-brick" or "resource:sandstone-brick" ||
        value is { Length: >= 1 and <= 64 } && ItemIdPattern().IsMatch(value);

    public static bool Equivalent(CharacterPossessionsSnapshot? first, CharacterPossessionsSnapshot? second)
    {
        if (first is null || second is null) return first is null && second is null;
        if (!IsValid(first) || !IsValid(second)) return false;
        return first.SchemaVersion == second.SchemaVersion &&
            first.Bags.SlotCapacity == second.Bags.SlotCapacity && first.Bank.SlotCapacity == second.Bank.SlotCapacity &&
            first.Bags.Slots.OrderBy(item => item.Slot).SequenceEqual(second.Bags.Slots.OrderBy(item => item.Slot)) &&
            first.Bank.Slots.OrderBy(item => item.Slot).SequenceEqual(second.Bank.Slots.OrderBy(item => item.Slot)) &&
            first.Equipment.OrderBy(item => item.Slot, StringComparer.Ordinal).SequenceEqual(second.Equipment.OrderBy(item => item.Slot, StringComparer.Ordinal)) &&
            EquivalentTool(first.HarvestTool, second.HarvestTool);
    }

    private static bool EquivalentTool(CharacterHarvestToolSnapshot? first, CharacterHarvestToolSnapshot? second) =>
        first is null || second is null ? first is null && second is null :
        first.Family == second.Family && first.Level == second.Level && first.BaseYieldBonusBps == second.BaseYieldBonusBps &&
        first.YieldBonusPerLevelBps == second.YieldBonusPerLevelBps && first.SocketCapacity == second.SocketCapacity &&
        first.Stones.SequenceEqual(second.Stones);

    [GeneratedRegex("\\A[a-z0-9]+(?:-[a-z0-9]+)*\\z", RegexOptions.CultureInvariant, 100)]
    private static partial Regex ItemIdPattern();
}
