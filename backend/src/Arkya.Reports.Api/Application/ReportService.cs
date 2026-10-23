using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Arkya.Reports.Api.Domain;
using Arkya.ServiceDefaults.Reports;

namespace Arkya.Reports.Api.Application;

public enum ReportChangeStatus { Created, Existing, Updated, Invalid, Conflict, NotFound, TooLarge }
public sealed record ReportChangeResult(ReportChangeStatus Status, BugReportResponse? Report = null,
    Dictionary<string, string[]>? Errors = null);

public sealed class ReportService(IReportRepository repository, TimeProvider clock)
{
    public async Task<ReportChangeResult> SubmitAsync(Guid accountId, SubmitBugReportRequest request, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (!BugReportKinds.All.Contains(request.Kind)) errors["kind"] = ["Use bug or suggestion."];
        if (request.ClientReportId == Guid.Empty) errors["clientReportId"] = ["A nonempty UUID is required."];
        Text(errors, "title", request.Title, 120, true);
        Text(errors, "description", request.Description, 4000, true);
        Text(errors, "clientVersion", request.ClientVersion, 128);
        Text(errors, "scene", request.Scene, 128);
        Text(errors, "platform", request.Platform, 64);
        if (request.ScreenshotBase64 is { } encoded &&
            (encoded.Length > PngScreenshot.MaximumBase64Characters ||
                encoded.Length / 4L * 3 - (encoded.EndsWith("==", StringComparison.Ordinal) ? 2 :
                    encoded.EndsWith('=') ? 1 : 0) > PngScreenshot.MaximumBytes))
            return new(ReportChangeStatus.TooLarge);
        if (!PngScreenshot.TryRead(request.ScreenshotBase64, out var image, out var metadata))
            errors["screenshotBase64"] = ["A valid non-interlaced RGB/RGBA 8-bit PNG, up to 2 MiB and 1920x1080, is required."];
        if (errors.Count != 0) return new(ReportChangeStatus.Invalid, Errors: errors);

        var title = request.Title!.Trim();
        var description = request.Description!.Trim();
        var version = request.ClientVersion!.Trim();
        var scene = request.Scene!.Trim();
        var platform = request.Platform!.Trim();
        // Preserve the original bug fingerprint so pre-kind clients can safely retry stored reports.
        var payload = request.Kind == BugReportKinds.Bug
            ? JsonSerializer.SerializeToUtf8Bytes(new { title, description, version, scene, platform, screenshot = metadata!.Sha256 })
            : JsonSerializer.SerializeToUtf8Bytes(new { title, description, version, scene, platform, screenshot = metadata!.Sha256, kind = request.Kind });
        var hash = Convert.ToHexStringLower(SHA256.HashData(payload));
        var now = clock.GetUtcNow();
        var report = new BugReportResponse(1, Guid.NewGuid(), request.ClientReportId, accountId, title, description,
            version, scene, platform, BugReportStatuses.New, [], "", 1, now, now, metadata!, [], request.Kind!);
        var insertion = await repository.InsertAsync(new(report, hash, image!), cancellationToken);
        return insertion.Report.PayloadHash != hash
            ? new(ReportChangeStatus.Conflict)
            : new(insertion.Created ? ReportChangeStatus.Created : ReportChangeStatus.Existing, insertion.Report.Report);
    }

    public async Task<ReportChangeResult> UpdateAsync(Guid id, UpdateBugReportRequest request, string actor, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.ExpectedRevision is < 1 or long.MaxValue)
            errors["expectedRevision"] = ["A positive, incrementable revision is required."];
        if (!BugReportStatuses.All.Contains(request.Status))
            errors["status"] = ["Unknown report status."];
        Text(errors, "resolution", request.Resolution, 4000);
        var tags = request.Tags?.Select(value => value?.Trim().ToLowerInvariant()).ToArray();
        if (tags is null || tags.Length > 10 || tags.Any(value => !ValidTag(value)))
            errors["tags"] = ["At most 10 tags, each 1–32 lowercase letters, digits, underscores or hyphens, are allowed."];
        if (!ValidActor(actor)) errors["actor"] = ["A printable administrator identifier up to 128 characters is required."];
        if (errors.Count != 0) return new(ReportChangeStatus.Invalid, Errors: errors);
        var updated = await repository.UpdateAsync(id, request.ExpectedRevision, request.Status!,
            tags!.Distinct(StringComparer.Ordinal).Select(value => value!).ToArray(), request.Resolution!.Trim(),
            actor, clock.GetUtcNow(), cancellationToken);
        if (updated is not null) return new(ReportChangeStatus.Updated, updated);
        return await repository.FindAsync(id, null, cancellationToken) is null
            ? new(ReportChangeStatus.NotFound) : new(ReportChangeStatus.Conflict);
    }

    public static bool ValidTag(string? value) => value is { Length: >= 1 and <= 32 } &&
        Regex.IsMatch(value, "\\A[a-z0-9][a-z0-9_-]*\\z", RegexOptions.CultureInvariant);

    public static bool ValidActor(string? value) => value is { Length: >= 1 and <= 128 } &&
        !string.IsNullOrWhiteSpace(value) && !value.Any(char.IsControl);

    private static void Text(Dictionary<string, string[]> errors, string field, string? value, int maximum, bool nonempty = false)
    {
        if (value is null || value.Length > maximum || nonempty && string.IsNullOrWhiteSpace(value) ||
            value.Any(character => char.IsControl(character) && character is not ('\r' or '\n' or '\t')))
            errors[field] = [$"A {(nonempty ? "nonempty " : "")}string up to {maximum} characters is required."];
    }
}
