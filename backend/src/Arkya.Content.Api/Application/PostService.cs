using System.Text.RegularExpressions;
using Arkya.Content.Api.Contracts;
using Arkya.Content.Api.Domain;

namespace Arkya.Content.Api.Application;

public enum PostChangeStatus { Saved, Invalid, NotFound, Conflict }
public sealed record PostChangeResult(PostChangeStatus Status, ContentPost? Post = null, Dictionary<string, string[]>? Errors = null);

public sealed partial class PostService(IPostRepository repository, TimeProvider clock)
{
    public const int MaxTitleLength = 160;
    public const int MaxSummaryLength = 400;
    public const int MaxBodyLength = 20000;
    public const int MaxGameVersionLength = 64;
    public const int MaxPageSize = 50;
    public const int MaxOffset = 10000;

    public Task<ContentPost?> FindAsync(Guid id, CancellationToken ct) => repository.FindAsync(id, ct);
    public Task<IReadOnlyList<ContentPost>> ListAsync(bool publishedOnly, string? kind, string? language, int limit, int offset,
        CancellationToken ct) => repository.ListAsync(publishedOnly, kind, language, limit + 1, offset, ct);

    public async Task<PostChangeResult> CreateAsync(CreatePostRequest request, CancellationToken ct)
    {
        var errors = Validate(request);
        if (errors.Count > 0) return new(PostChangeStatus.Invalid, Errors: errors);
        var now = clock.GetUtcNow();
        var post = new ContentPost(Guid.NewGuid(), 1, Content(request), now, now, null);
        await repository.CreateAsync(post, ct);
        return new(PostChangeStatus.Saved, post);
    }

    public async Task<PostChangeResult> UpdateAsync(Guid id, UpdatePostRequest request, CancellationToken ct)
    {
        var content = new CreatePostRequest(request.Kind, request.Language, request.Title, request.Summary, request.Body, request.GameVersion);
        var errors = Validate(content);
        ValidateRevision(request.Revision, errors);
        if (errors.Count > 0) return new(PostChangeStatus.Invalid, Errors: errors);
        return await ChangeAsync(id, request.Revision!.Value, post => post with { Draft = Content(content) }, ct);
    }

    public async Task<PostChangeResult> PublishAsync(Guid id, long? revision, bool publish, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        ValidateRevision(revision, errors);
        if (errors.Count > 0) return new(PostChangeStatus.Invalid, Errors: errors);
        return await ChangeAsync(id, revision!.Value, post => post with
        {
            Published = publish ? new PublishedPost(post.Revision, post.UpdatedAt, post.Draft) : null
        }, ct);
    }

    public async Task<PostChangeResult> DeleteAsync(Guid id, long? revision, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        ValidateRevision(revision, errors);
        if (errors.Count > 0) return new(PostChangeStatus.Invalid, Errors: errors);
        if (await repository.FindAsync(id, ct) is null) return new(PostChangeStatus.NotFound);
        return new(await repository.TryDeleteAsync(id, revision!.Value, ct) ? PostChangeStatus.Saved : PostChangeStatus.Conflict);
    }

    private async Task<PostChangeResult> ChangeAsync(Guid id, long revision, Func<ContentPost, ContentPost> change, CancellationToken ct)
    {
        var existing = await repository.FindAsync(id, ct);
        if (existing is null) return new(PostChangeStatus.NotFound);
        if (existing.Revision != revision) return new(PostChangeStatus.Conflict);
        var replacement = change(existing with { Revision = revision + 1, UpdatedAt = clock.GetUtcNow() });
        return await repository.TryReplaceAsync(revision, replacement, ct)
            ? new(PostChangeStatus.Saved, replacement) : new(PostChangeStatus.Conflict);
    }

    public static Dictionary<string, string[]> ValidateQuery(string? kind, string? language, int limit, int offset)
    {
        var errors = new Dictionary<string, string[]>();
        if (kind is not null && kind is not ("news" or "release-note")) errors["kind"] = ["Use news or release-note."];
        if (language is not null && language is not ("fr" or "en")) errors["language"] = ["Use fr or en."];
        if (limit is < 1 or > MaxPageSize) errors["limit"] = [$"Use a limit from 1 to {MaxPageSize}."];
        if (offset is < 0 or > MaxOffset) errors["offset"] = [$"Use an offset from 0 to {MaxOffset}."];
        return errors;
    }

    private static Dictionary<string, string[]> Validate(CreatePostRequest request)
    {
        var errors = ValidateQuery(request.Kind, request.Language, 1, 0);
        if (request.Kind is null) errors["kind"] = ["A kind is required."];
        if (request.Language is null) errors["language"] = ["A language is required."];
        ValidateText(request.Title, MaxTitleLength, "title", false, errors);
        if (request.Summary is not null && (request.Summary.Length > MaxSummaryLength || request.Summary.Any(char.IsControl)))
            errors["summary"] = [$"Provide a summary of at most {MaxSummaryLength} characters without control characters."];
        ValidateText(request.Body, MaxBodyLength, "body", true, errors);
        if (request.Kind == "release-note" && (request.GameVersion is null || request.GameVersion.Length is < 1 or > MaxGameVersionLength ||
            !GameVersionPattern().IsMatch(request.GameVersion)))
            errors["gameVersion"] = ["A release note requires a version of 1 to 64 ASCII letters, digits, dots, underscores, plus signs or hyphens, starting with a letter or digit."];
        if (request.Kind == "news" && !string.IsNullOrEmpty(request.GameVersion))
            errors["gameVersion"] = ["Only release notes have a game version."];
        return errors;
    }

    private static void ValidateText(string? text, int maxLength, string name, bool multiline, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > maxLength ||
            text.Any(character => char.IsControl(character) && !(multiline && character is '\r' or '\n' or '\t')))
            errors[name] = [$"Provide text of 1 to {maxLength} characters without unsupported control characters."];
    }

    private static void ValidateRevision(long? revision, Dictionary<string, string[]> errors)
    {
        if (revision is null or < 1 or long.MaxValue) errors["revision"] = ["An explicit positive revision below Int64.MaxValue is required."];
    }

    private static PostContent Content(CreatePostRequest request) => new(request.Kind!, request.Language!, request.Title!.Trim(),
        request.Summary?.Trim() ?? "", request.Body!.Trim(), request.Kind == "release-note" ? request.GameVersion : null);

    [GeneratedRegex("\\A[A-Za-z0-9][A-Za-z0-9._+-]*\\z", RegexOptions.CultureInvariant, 100)]
    private static partial Regex GameVersionPattern();
}
