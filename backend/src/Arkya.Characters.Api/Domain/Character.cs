namespace Arkya.Characters.Api.Domain;

public sealed record Character(Guid Id, Guid AccountId, string Name, string NormalizedName,
    int Level, DateTimeOffset CreatedAt)
{
    public CharacterAppearance Appearance { get; init; } = CharacterAppearance.Default;
}
