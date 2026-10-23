using System.Text;
using System.Text.Json;
using Arkya.Characters.Api.Contracts;
using Arkya.ServiceDefaults.Characters;

namespace Arkya.Characters.Api.Application;

public sealed class CharacterAdminDetailService(ICharacterAdminRepository admin, ICharacterRepository characters,
    IGameLeaseStore leases, TimeProvider clock)
{
    public async Task<AdminCharacterDetail?> GetAsync(Guid characterId, string? realmId, int questPage,
        int questPageSize, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var summary = await admin.FindAsync(characterId, realmId, now, cancellationToken);
        if (summary is null) return null;
        var character = await characters.FindOwnedAsync(characterId, Guid.Parse(summary.AccountId), cancellationToken);
        if (character is null || realmId is not null && character.RealmId != realmId) return null;
        var lease = await leases.FindCharacterAsync(characterId, cancellationToken);
        if (lease is not null && (lease.AccountId != character.AccountId || lease.RealmId != character.RealmId)) lease = null;
        var quests = await admin.ListQuestsAsync(characterId, questPage, questPageSize, cancellationToken);
        var appearance = character.Appearance;
        var world = lease?.State is { } state ? new AdminCharacterWorldState(state.Revision, lease.ServerId,
            lease.Area?.AreaId, lease.Area?.ContentId, state.IslandId, state.Position.X, state.Position.Y,
            state.Position.Z, state.Forward.X, state.Forward.Z) : null;
        return new(summary, character.Level, new(appearance.Version, appearance.Race, appearance.Silhouette,
            appearance.HeightCm, appearance.Skin, appearance.HairStyle, appearance.HairColor, appearance.EyeColor,
            appearance.OutfitColor), now, lease?.LastConnectedAt, world,
            CharacterAdminGameplaySnapshot.Read(lease?.State?.Simulation), quests,
            new("not_persisted"), new("not_persisted"), new("not_persisted"));
    }
}

public static class CharacterAdminGameplaySnapshot
{
    private static readonly string[] ClassNames = ["warrior", "scout", "mage", "cleric", "druid"];
    private static readonly string[] ClassFields = ["Warrior", "Scout", "Mage", "Cleric", "Druid"];

    public static AdminCharacterGameplay Read(ActorStateEnvelope? envelope)
    {
        if (envelope is null) return Unavailable("not_saved");
        if (envelope.SchemaVersion != 1) return Unavailable("unsupported");
        if (string.IsNullOrEmpty(envelope.Payload) || Encoding.UTF8.GetByteCount(envelope.Payload) > 32768)
            return Unavailable("invalid");
        try
        {
            using var document = JsonDocument.Parse(envelope.Payload, new JsonDocumentOptions { MaxDepth = 16 });
            var actor = document.RootElement;
            if (!Int(actor, "version", out var version)) return Unavailable("invalid");
            if (version == 1) return Unavailable("not_saved");
            if (version != 2) return Unavailable("unsupported");
            if (!Object(actor, "gameplay", out var gameplay) || !Int(gameplay, "version", out var gameplayVersion))
                return Unavailable("invalid");
            if (gameplayVersion != 1) return Unavailable("unsupported");
            if (!Object(gameplay, "value", out var value) || !Int(value, "ActiveClass", out var activeClass) ||
                activeClass is < 1 or > 5 || !Vital(value, "Health", "MaxHealth", out var health, out var maxHealth) ||
                !Vital(value, "Mana", "MaxMana", out var mana, out var maxMana) ||
                !Vital(value, "Stamina", "MaxStamina", out var stamina, out var maxStamina) ||
                !UInt(value, "LearnedRankOne", out var rankOne) || !UInt(value, "LearnedRankTwo", out var rankTwo) ||
                ((rankOne | rankTwo) & ~((1u << 25) - 1)) != 0 || (rankTwo & ~rankOne) != 0)
                return Unavailable("invalid");
            var classes = new AdminCharacterClassProgress[ClassNames.Length];
            // Gameplay version 1 uses the prototype curve: level * 100 XP before the next level.
            for (var index = 0; index < classes.Length; index++)
            {
                if (!Int(value, ClassFields[index] + "Level", out var level) || level is < 0 or > 10 ||
                    !Int(value, ClassFields[index] + "Experience", out var experience) || experience < 0 ||
                    (level is 0 or 10 ? experience != 0 : experience >= level * 100) ||
                    level == 0 && activeClass == index + 1)
                    return Unavailable("invalid");
                classes[index] = new(ClassNames[index], level, experience);
            }
            return new("available", new(health, maxHealth, mana, maxMana, stamina, maxStamina),
                ClassNames[activeClass - 1], classes, rankOne, rankTwo);
        }
        catch (JsonException) { return Unavailable("invalid"); }
    }

    private static AdminCharacterGameplay Unavailable(string status) => new(status, null, null, [], null, null);
    private static bool Object(JsonElement owner, string name, out JsonElement value)
    {
        value = default;
        return owner.ValueKind == JsonValueKind.Object && owner.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.Object;
    }
    private static bool Int(JsonElement owner, string name, out int value)
    {
        value = 0;
        return owner.ValueKind == JsonValueKind.Object && owner.TryGetProperty(name, out var property) &&
            property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out value);
    }
    private static bool UInt(JsonElement owner, string name, out uint value)
    {
        value = 0;
        return owner.ValueKind == JsonValueKind.Object && owner.TryGetProperty(name, out var property) &&
            property.ValueKind == JsonValueKind.Number && property.TryGetUInt32(out value);
    }
    private static bool Vital(JsonElement owner, string currentField, string maximumField, out int current, out int maximum)
    {
        current = maximum = 0;
        return Int(owner, currentField, out current) && Int(owner, maximumField, out maximum) &&
            maximum > 0 && current >= 0 && current <= maximum;
    }
}
