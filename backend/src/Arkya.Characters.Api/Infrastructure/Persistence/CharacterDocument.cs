using Arkya.Characters.Api.Domain;
using MongoDB.Bson.Serialization.Attributes;

namespace Arkya.Characters.Api.Infrastructure.Persistence;

public sealed class CharacterDocument
{
    [BsonId]
    public string Id { get; set; } = "";
    public int SchemaVersion { get; set; } = 1;
    public string AccountId { get; set; } = "";
    public string Name { get; set; } = "";
    public string NormalizedName { get; set; } = "";
    public int Level { get; set; }
    public DateTime CreatedAt { get; set; }
    public CharacterAppearanceDocument? Appearance { get; set; }

    public Character ToDomain() => new(Guid.Parse(Id), Guid.Parse(AccountId), Name, NormalizedName,
        Level, new DateTimeOffset(DateTime.SpecifyKind(CreatedAt, DateTimeKind.Utc)))
    {
        Appearance = ReadAppearance()
    };

    private CharacterAppearance ReadAppearance()
    {
        // Version 1 characters predate customization and retain a stable visual identity.
        if (SchemaVersion == 1 && Appearance is null)
            return CharacterAppearance.Default;
        if (SchemaVersion != 2 || Appearance is null)
            throw new InvalidDataException("Unsupported or incomplete character document schema.");
        var appearance = Appearance.ToDomain();
        if (appearance.Validate().Count > 0)
            throw new InvalidDataException("Character document contains an unsupported appearance.");
        return appearance;
    }

    public static CharacterDocument From(Character character) => new()
    {
        SchemaVersion = 2,
        Id = character.Id.ToString("D"),
        AccountId = character.AccountId.ToString("D"),
        Name = character.Name,
        NormalizedName = character.NormalizedName,
        Level = character.Level,
        CreatedAt = character.CreatedAt.UtcDateTime,
        Appearance = CharacterAppearanceDocument.From(character.Appearance)
    };
}
