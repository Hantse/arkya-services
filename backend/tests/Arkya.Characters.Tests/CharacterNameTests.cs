using Arkya.Characters.Api.Domain;

namespace Arkya.Characters.Tests;

public sealed class CharacterNameTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("abcdefghijklmnopqrstu")]
    [InlineData("Élodie")]
    [InlineData("Alic e")]
    [InlineData("Alic3")]
    [InlineData("Ali-ce")]
    public void RejectsNamesOutsideThePolicy(string? value)
    {
        Assert.False(CharacterName.TryCreate(value, out var name));
        Assert.Null(name);
    }

    [Theory]
    [InlineData("  aLiCe  ", "aLiCe", "ALICE")]
    [InlineData("Abc", "Abc", "ABC")]
    [InlineData("abcdefghijklmnopqrst", "abcdefghijklmnopqrst", "ABCDEFGHIJKLMNOPQRST")]
    public void PreservesDisplayCaseAndNormalizesForUniqueness(string input, string display, string normalized)
    {
        Assert.True(CharacterName.TryCreate(input, out var name));
        Assert.Equal(display, name!.Value);
        Assert.Equal(normalized, name.NormalizedValue);
    }
}
