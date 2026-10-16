using Arkya.Characters.Api.Domain;

namespace Arkya.Characters.Api.Infrastructure.Persistence;

public sealed class CharacterAppearanceDocument
{
    public int Version { get; set; }
    public string Race { get; set; } = "";
    public string Silhouette { get; set; } = "";
    public int HeightCm { get; set; }
    public string Skin { get; set; } = "";
    public string HairStyle { get; set; } = "";
    public string HairColor { get; set; } = "";
    public string EyeColor { get; set; } = "";
    public string OutfitColor { get; set; } = "";

    public CharacterAppearance ToDomain() => new(Version, Race, Silhouette, HeightCm,
        Skin, HairStyle, HairColor, EyeColor, OutfitColor);

    public static CharacterAppearanceDocument From(CharacterAppearance appearance) => new()
    {
        Version = appearance.Version, Race = appearance.Race, Silhouette = appearance.Silhouette,
        HeightCm = appearance.HeightCm, Skin = appearance.Skin, HairStyle = appearance.HairStyle,
        HairColor = appearance.HairColor, EyeColor = appearance.EyeColor, OutfitColor = appearance.OutfitColor
    };
}
