namespace Arkya.Characters.Api.Domain;

public sealed record CharacterName
{
    public const string ValidationMessage = "The character name must contain 3 to 20 ASCII letters (A-Z).";

    public string Value { get; }
    public string NormalizedValue { get; }

    private CharacterName(string value)
    {
        Value = value;
        NormalizedValue = value.ToUpperInvariant();
    }

    public static bool TryCreate(string? value, out CharacterName? name)
    {
        var trimmed = value?.Trim();
        if (trimmed is null || trimmed.Length is < 3 or > 20 ||
            trimmed.Any(character => character is not (>= 'A' and <= 'Z') and not (>= 'a' and <= 'z')))
        {
            name = null;
            return false;
        }

        name = new CharacterName(trimmed);
        return true;
    }
}
