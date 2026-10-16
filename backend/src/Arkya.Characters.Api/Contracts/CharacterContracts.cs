using Arkya.Characters.Api.Domain;

namespace Arkya.Characters.Api.Contracts;

public sealed record CreateCharacterRequest(string? Name, CharacterAppearanceRequest? Appearance = null);
public sealed record CharacterAppearanceRequest(int Version, string? Race, string? Silhouette, int HeightCm,
    string? Skin, string? HairStyle, string? HairColor, string? EyeColor, string? OutfitColor)
{
    public CharacterAppearance ToDomain() => new(Version, Race ?? "", Silhouette ?? "", HeightCm,
        Skin ?? "", HairStyle ?? "", HairColor ?? "", EyeColor ?? "", OutfitColor ?? "");
}

public sealed record CharacterAppearanceResponse(int Version, string Race, string Silhouette, int HeightCm,
    string Skin, string HairStyle, string HairColor, string EyeColor, string OutfitColor)
{
    public static CharacterAppearanceResponse From(CharacterAppearance appearance) => new(appearance.Version,
        appearance.Race, appearance.Silhouette, appearance.HeightCm, appearance.Skin, appearance.HairStyle,
        appearance.HairColor, appearance.EyeColor, appearance.OutfitColor);
}

public sealed record ResolveCharacterRequest(string? SessionToken);

public sealed record CharacterResponse(Guid Id, string Name, int Level, DateTimeOffset CreatedAt,
    CharacterAppearanceResponse Appearance)
{
    public static CharacterResponse From(Character character) =>
        new(character.Id, character.Name, character.Level, character.CreatedAt,
            CharacterAppearanceResponse.From(character.Appearance));
}

public sealed record ResolvedCharacterResponse(Guid AccountId, CharacterResponse Character, DateTimeOffset SessionExpiresAt);
