using Arkya.Content.Api.Domain;

namespace Arkya.Content.Api.Application;

public static class ItemMobCatalog
{
    // Keep authoring tied to enemy identities actually supported by the gameplay slice.
    public static IReadOnlyList<ItemMobDefinition> All { get; } = Array.AsReadOnly<ItemMobDefinition>(
        [new(ContentService.SupportedEnemyId, "Prototype monster")]);

    public static bool Contains(string? mobId) => All.Any(mob => mob.MobId == mobId);
}
