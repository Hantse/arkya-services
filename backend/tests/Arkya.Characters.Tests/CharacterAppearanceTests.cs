using System.Text.Json;
using Arkya.Characters.Api.Application;
using Arkya.Characters.Api.Contracts;
using Arkya.Characters.Api.Domain;
using Arkya.Characters.Api.Infrastructure.Persistence;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace Arkya.Characters.Tests;

public sealed class CharacterAppearanceTests
{
    public static IEnumerable<object[]> InvalidAppearances()
    {
        var appearance = CharacterAppearance.Default;
        yield return [appearance with { Version = 0 }, "version"];
        yield return [appearance with { Version = 2 }, "version"];
        yield return [appearance with { Race = "orc" }, "race"];
        yield return [appearance with { Race = "Human" }, "race"];
        yield return [appearance with { Silhouette = "c" }, "silhouette"];
        yield return [appearance with { HeightCm = 159 }, "heightCm"];
        yield return [appearance with { HeightCm = 196 }, "heightCm"];
        yield return [appearance with { Race = "elf", HeightCm = 169 }, "heightCm"];
        yield return [appearance with { Race = "elf", HeightCm = 206 }, "heightCm"];
        yield return [appearance with { Skin = "#ff0000" }, "skin"];
        yield return [appearance with { HairStyle = "asset/path" }, "hairStyle"];
        yield return [appearance with { HairColor = "invalid" }, "hairColor"];
        yield return [appearance with { EyeColor = "invalid" }, "eyeColor"];
        yield return [appearance with { OutfitColor = "invalid" }, "outfitColor"];
    }

    [Theory]
    [MemberData(nameof(InvalidAppearances))]
    public async Task UnsupportedOptionsCannotReachPersistence(CharacterAppearance appearance, string field)
    {
        var repository = new MemoryCharacterRepository();
        var service = new CharacterService(repository, new FixedTimeProvider());
        var ownerId = Guid.NewGuid();
        var result = await service.CreateAsync(ownerId, "Aeria", appearance, CancellationToken.None);

        Assert.Equal(CreateCharacterStatus.InvalidAppearance, result.Status);
        Assert.Contains($"appearance.{field}", result.Errors!.Keys);
        Assert.Empty(await repository.ListOwnedAsync(ownerId, CancellationToken.None));
    }

    [Theory]
    [InlineData("human", 160)]
    [InlineData("human", 195)]
    [InlineData("elf", 170)]
    [InlineData("elf", 205)]
    public void RaceHeightBoundariesAreInclusive(string race, int heightCm) =>
        Assert.Empty((CharacterAppearance.Default with { Race = race, HeightCm = heightCm }).Validate());

    [Fact]
    public async Task ReferencePaletteCanBeCreatedAndReadWithoutChangingLegacyDefaults()
    {
        var repository = new MemoryCharacterRepository();
        var service = new CharacterService(repository, new FixedTimeProvider());
        var ownerId = Guid.NewGuid();
        var appearance = CharacterAppearance.Default with
        {
            Silhouette = "b", Skin = "sand", HairStyle = "braid", HairColor = "magenta", OutfitColor = "jade"
        };
        var result = await service.CreateAsync(ownerId, "Aeria", appearance, CancellationToken.None);
        Assert.Equal(CreateCharacterStatus.Created, result.Status);
        var saved = Assert.Single(await repository.ListOwnedAsync(ownerId, CancellationToken.None));
        Assert.Equal(appearance, saved.Appearance);
        var restored = BsonSerializer.Deserialize<CharacterDocument>(CharacterDocument.From(saved).ToBsonDocument()).ToDomain();
        Assert.Equal(appearance, restored.Appearance);
        Assert.Equal("brown", CharacterAppearance.Default.HairColor);
        Assert.Equal("sage", CharacterAppearance.Default.OutfitColor);
    }

    [Fact]
    public void BsonRoundTripPreservesAllAppearanceFieldsAndUpgradesSchema()
    {
        var character = new Character(Guid.NewGuid(), Guid.NewGuid(), "Aeria", "AERIA", 1, FixedTimeProvider.Now)
        {
            Appearance = new(1, "elf", "b", 198, "ebony", "braid", "silver", "violet", "wine")
        };
        var bson = CharacterDocument.From(character).ToBsonDocument();

        Assert.Equal(2, bson["SchemaVersion"].AsInt32);
        Assert.Equal("elf", bson["Appearance"]["Race"].AsString);
        Assert.Equal(character, BsonSerializer.Deserialize<CharacterDocument>(bson).ToDomain());
    }

    [Fact]
    public void LegacyBsonDocumentReceivesStableDefaultWithoutChangingOwnership()
    {
        var original = new Character(Guid.NewGuid(), Guid.NewGuid(), "Aeria", "AERIA", 1, FixedTimeProvider.Now);
        var bson = CharacterDocument.From(original).ToBsonDocument();
        bson["SchemaVersion"] = 1;
        bson.Remove("Appearance");

        var migrated = BsonSerializer.Deserialize<CharacterDocument>(bson).ToDomain();

        Assert.Equal(original, migrated);
        Assert.Equal(CharacterAppearance.Default, migrated.Appearance);
        Assert.Equal(2, CharacterDocument.From(migrated).SchemaVersion);
    }

    [Fact]
    public void CorruptCurrentAndUnknownSchemasAreNotSilentlyReplacedWithDefaults()
    {
        var document = CharacterDocument.From(new Character(Guid.NewGuid(), Guid.NewGuid(), "Aeria", "AERIA", 1, FixedTimeProvider.Now));
        document.Appearance!.Race = "orc";
        Assert.Throws<InvalidDataException>(() => document.ToDomain());
        document.Appearance = null;
        Assert.Throws<InvalidDataException>(() => document.ToDomain());
        document.SchemaVersion = 3;
        Assert.Throws<InvalidDataException>(() => document.ToDomain());
    }

    [Fact]
    public void ServerCatalogMatchesTheCharacterArtManifest()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "character-manifest.json")));
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var root = manifest.RootElement;
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        Assert.Equal(JsonSerializer.Deserialize<CharacterAppearanceResponse>(root.GetProperty("defaults"), json),
            CharacterAppearanceResponse.From(CharacterAppearance.Default));
        Assert.Equal(root.GetProperty("races").EnumerateArray().Select(item => new AppearanceRaceOption(
            item.GetProperty("id").GetString()!, item.GetProperty("label").GetString()!,
            JsonSerializer.Deserialize<AppearanceHeightRange>(item.GetProperty("height"), json)!)), CharacterAppearanceCatalog.Races);
        Assert.Equal(JsonSerializer.Deserialize<AppearanceOption[]>(root.GetProperty("silhouettes"), json), CharacterAppearanceCatalog.Silhouettes);
        Assert.Equal(JsonSerializer.Deserialize<AppearanceOption[]>(root.GetProperty("hairStyles"), json), CharacterAppearanceCatalog.HairStyles);
        var palettes = root.GetProperty("palettes");
        Assert.Equal(JsonSerializer.Deserialize<AppearanceColorOption[]>(palettes.GetProperty("skin"), json), CharacterAppearanceCatalog.Skin);
        Assert.Equal(JsonSerializer.Deserialize<AppearanceColorOption[]>(palettes.GetProperty("hair"), json), CharacterAppearanceCatalog.Hair);
        Assert.Equal(JsonSerializer.Deserialize<AppearanceColorOption[]>(palettes.GetProperty("eyes"), json), CharacterAppearanceCatalog.Eyes);
        Assert.Equal(JsonSerializer.Deserialize<AppearanceColorOption[]>(palettes.GetProperty("outfit"), json), CharacterAppearanceCatalog.Outfit);
    }
}
