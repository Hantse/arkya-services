using System.Collections.Immutable;

namespace Arkya.Characters.Api.Domain;

public sealed record CharacterAppearance(int Version, string Race, string Silhouette, int HeightCm,
    string Skin, string HairStyle, string HairColor, string EyeColor, string OutfitColor)
{
    public static CharacterAppearance Default { get; } = new(1, "human", "a", 175,
        "warm", "short", "brown", "green", "sage");

    public Dictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>();
        if (Version != 1)
            errors["appearance.version"] = ["Only appearance version 1 is supported."];
        var race = CharacterAppearanceCatalog.Races.FirstOrDefault(option => option.Id == Race);
        if (race is null)
            errors["appearance.race"] = ["Choose a supported race."];
        else if (HeightCm < race.Height.Min || HeightCm > race.Height.Max)
            errors["appearance.heightCm"] = [$"Height must be between {race.Height.Min} and {race.Height.Max} cm for this race."];
        Check("silhouette", Silhouette, CharacterAppearanceCatalog.Silhouettes.Select(option => option.Id));
        Check("hairStyle", HairStyle, CharacterAppearanceCatalog.HairStyles.Select(option => option.Id));
        Check("skin", Skin, CharacterAppearanceCatalog.Skin.Select(option => option.Id));
        Check("hairColor", HairColor, CharacterAppearanceCatalog.Hair.Select(option => option.Id));
        Check("eyeColor", EyeColor, CharacterAppearanceCatalog.Eyes.Select(option => option.Id));
        Check("outfitColor", OutfitColor, CharacterAppearanceCatalog.Outfit.Select(option => option.Id));
        return errors;

        void Check(string field, string value, IEnumerable<string> allowed)
        {
            if (!allowed.Contains(value, StringComparer.Ordinal))
                errors[$"appearance.{field}"] = ["Choose a supported appearance option."];
        }
    }
}

public sealed record AppearanceOption(string Id, string Label);
public sealed record AppearanceColorOption(string Id, string Label, string Hex);
public sealed record AppearanceHeightRange(int Min, int Max, int Default);
public sealed record AppearanceRaceOption(string Id, string Label, AppearanceHeightRange Height);

// Stable IDs mirror the character art manifest; no gameplay stats come from appearance.
public static class CharacterAppearanceCatalog
{
    public static ImmutableArray<AppearanceRaceOption> Races { get; } =
    [
        new("human", "Humain", new(160, 195, 175)),
        new("elf", "Elfe", new(170, 205, 185))
    ];
    public static ImmutableArray<AppearanceOption> Silhouettes { get; } =
        [new("a", "Masculine"), new("b", "Féminine")];
    public static ImmutableArray<AppearanceOption> HairStyles { get; } =
        [new("short", "Courts"), new("long", "Longs"), new("braid", "Tressés")];
    public static ImmutableArray<AppearanceColorOption> Skin { get; } =
    [
        new("porcelain", "Porcelaine", "#f0d6c6"), new("rose", "Rosé", "#dfb8a4"),
        new("sand", "Sable", "#d7ad87"), new("warm", "Miel", "#bc8a65"),
        new("olive", "Olive", "#aa8969"), new("copper", "Cuivré", "#96664c"),
        new("umber", "Terre d’ombre", "#724e3e"), new("ebony", "Ébène", "#4a342d")
    ];
    public static ImmutableArray<AppearanceColorOption> Hair { get; } =
    [
        new("black", "Noir", "#272b2c"), new("brown", "Châtain", "#665044"),
        new("chestnut", "Châtaigne", "#885a3d"), new("auburn", "Auburn", "#b5784d"),
        new("blond", "Blond doré", "#d8bd7d"), new("ash", "Blond cendré", "#b9ad94"),
        new("silver", "Argent", "#c5d0ca"), new("white", "Blanc nacré", "#eee8d3"),
        new("magenta", "Magenta", "#a52e62")
    ];
    public static ImmutableArray<AppearanceColorOption> Eyes { get; } =
    [
        new("brown", "Brun", "#795132"), new("amber", "Ambre", "#c59346"),
        new("green", "Vert sauge", "#7c9c7b"), new("blue", "Bleu ardoise", "#6c9ba9"),
        new("gray", "Gris", "#a7b2ad"), new("violet", "Améthyste", "#9883aa")
    ];
    public static ImmutableArray<AppearanceColorOption> Outfit { get; } =
    [
        new("sage", "Sauge", "#7c9c7b"), new("ocean", "Océan", "#608d99"),
        new("ochre", "Ocre", "#bd965b"), new("wine", "Lie-de-vin", "#9d6e79"),
        new("jade", "Jade", "#159b8d")
    ];
}
