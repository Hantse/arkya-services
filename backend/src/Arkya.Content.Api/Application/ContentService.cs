using System.Text.RegularExpressions;
using Arkya.Content.Api.Contracts;
using Arkya.Content.Api.Domain;

namespace Arkya.Content.Api.Application;

public enum ContentChangeStatus { Saved, Published, Invalid, Conflict }

public sealed record ContentChangeResult(ContentChangeStatus Status, QuestDraft? Draft = null,
    PublishedCatalog? Catalog = null, Dictionary<string, string[]>? Errors = null);

public sealed partial class ContentService(IContentRepository repository, TimeProvider clock)
{
    public const int MaxQuests = 128;
    public const int MaxTitleLength = 120;
    public const int MaxDescriptionLength = 2000;
    public const string SupportedEnemyId = "prototype-monster";

    public Task<QuestDraft> GetDraftAsync(CancellationToken cancellationToken) => repository.GetDraftAsync(cancellationToken);
    public Task<PublishedCatalog?> FindCatalogAsync(Guid version, CancellationToken cancellationToken) =>
        repository.FindCatalogAsync(version, cancellationToken);
    public Task<IReadOnlyList<PublishedCatalog>> ListCatalogsAsync(CancellationToken cancellationToken) =>
        repository.ListCatalogsAsync(cancellationToken);

    public async Task<ContentChangeResult> SaveDraftAsync(SaveDraftRequest request, CancellationToken cancellationToken)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
            return new(ContentChangeStatus.Invalid, Errors: errors);

        var quests = request.Quests!.Select(quest => new QuestDefinition(quest!.QuestId!, quest.Title!.Trim(),
            quest.Description!, quest.EnemyId!, quest.RequiredKills)).ToArray();
        var draft = await repository.TrySaveDraftAsync(request.Revision!.Value, quests, cancellationToken);
        return draft is null ? new(ContentChangeStatus.Conflict) : new(ContentChangeStatus.Saved, Draft: draft);
    }

    public async Task<ContentChangeResult> PublishAsync(PublishCatalogRequest request, CancellationToken cancellationToken)
    {
        if (request.DraftRevision is null or < 0)
            return new(ContentChangeStatus.Invalid, Errors: new() { ["draftRevision"] = ["A non-negative draft revision is required."] });

        var draft = await repository.GetDraftAsync(cancellationToken);
        if (draft.Revision == request.DraftRevision && draft.Quests.Count == 0)
            return new(ContentChangeStatus.Invalid, Errors: new() { ["quests"] = ["At least one quest is required to publish a catalog."] });

        var result = await repository.PublishAsync(request.DraftRevision.Value, clock.GetUtcNow(), cancellationToken);
        return result.Status == PublicationStatus.Published
            ? new(ContentChangeStatus.Published, Catalog: result.Catalog)
            : new(ContentChangeStatus.Conflict);
    }

    private static Dictionary<string, string[]> Validate(SaveDraftRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.Revision is null or < 0 or long.MaxValue)
            errors["revision"] = ["A non-negative revision below Int64.MaxValue is required."];
        if (request.Quests is null || request.Quests.Length > MaxQuests)
        {
            errors["quests"] = [$"Provide an array with at most {MaxQuests} quests."];
            return errors;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < request.Quests.Length; index++)
        {
            var quest = request.Quests[index];
            var prefix = $"quests[{index}]";
            if (quest is null)
            {
                errors[prefix] = ["A quest is required."];
                continue;
            }
            if (quest.QuestId is null || quest.QuestId.Length is < 1 or > 64 || !QuestIdPattern().IsMatch(quest.QuestId))
                errors[$"{prefix}.questId"] = ["Use a stable lowercase ASCII slug of 1 to 64 characters."];
            else if (!ids.Add(quest.QuestId))
                errors[$"{prefix}.questId"] = ["Quest IDs must be unique within the catalog."];
            if (string.IsNullOrWhiteSpace(quest.Title) || quest.Title.Length > MaxTitleLength || quest.Title.Any(char.IsControl))
                errors[$"{prefix}.title"] = [$"Provide a title of 1 to {MaxTitleLength} characters without control characters."];
            if (quest.Description is null || quest.Description.Length > MaxDescriptionLength ||
                quest.Description.Any(character => char.IsControl(character) && character is not ('\n' or '\r' or '\t')))
                errors[$"{prefix}.description"] = [$"Provide a description of at most {MaxDescriptionLength} characters."];
            if (quest.EnemyId != SupportedEnemyId)
                errors[$"{prefix}.enemyId"] = [$"Only {SupportedEnemyId} is supported by this gameplay slice."];
            if (quest.RequiredKills is < 1 or > 10000)
                errors[$"{prefix}.requiredKills"] = ["Required kills must be between 1 and 10000."];
        }
        return errors;
    }

    [GeneratedRegex("\\A[a-z0-9]+(?:-[a-z0-9]+)*\\z", RegexOptions.CultureInvariant, 100)]
    private static partial Regex QuestIdPattern();
}
