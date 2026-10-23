using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Arkya.Admin.Content;

public sealed class ContentApiOptions
{
    public bool Enabled { get; init; } = true;
    public string BaseUrl { get; init; } = "http://localhost:5143";
    public string AdminKey { get; init; } = "";
    public bool AllowClusterHttp { get; init; }

    public static bool IsSafe(string value, bool development, bool allowClusterHttp = false) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment)
        && (uri.Scheme == "https" || development && uri.Scheme == "http" && uri.IsLoopback ||
            allowClusterHttp && uri.Scheme == "http" && Regex.IsMatch(value,
                @"\Ahttp://[a-z](?:[a-z0-9-]{0,61}[a-z0-9])?\.[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.svc\.cluster\.local(?::[1-9][0-9]{0,4})?/?\z",
                RegexOptions.CultureInvariant));
}

public sealed record QuestDefinition(string QuestId, string Title, string Description, string EnemyId, int RequiredKills);
public sealed record ContentDraft(long Revision, QuestDefinition[] Quests);
public sealed record ContentCatalog(int SchemaVersion, string Version, DateTimeOffset PublishedAt, QuestDefinition[] Quests);

public sealed class ContentApiException(HttpStatusCode statusCode, string message) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}

public sealed partial class ContentAdminClient(HttpClient client, IOptions<ContentApiOptions> options)
{
    public bool Enabled => options.Value.Enabled;
    public Task<ContentDraft> GetDraftAsync(CancellationToken ct) => SendAsync<ContentDraft>(HttpMethod.Get, "admin/content/draft", null, ct);
    public Task<ContentCatalog[]> GetCatalogsAsync(CancellationToken ct) => SendAsync<ContentCatalog[]>(HttpMethod.Get, "admin/content/catalogs", null, ct);
    public Task<ContentDraft> SaveAsync(ContentDraft draft, CancellationToken ct) => SendAsync<ContentDraft>(HttpMethod.Put, "admin/content/draft", draft, ct);
    public Task<ContentCatalog> PublishAsync(long revision, CancellationToken ct) => SendAsync<ContentCatalog>(HttpMethod.Post,
        "admin/content/catalogs", new { draftRevision = revision }, ct);

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct, bool publication = false)
    {
        if (!options.Value.Enabled)
            throw new ContentApiException(HttpStatusCode.ServiceUnavailable, publication
                ? "Publication management is disabled on this installation."
                : "The quest workshop is disabled on this installation.");
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Arkya-Admin-Key", options.Value.AdminKey);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var message = publication ? ContentPublicationPresentation.ErrorMessage(response.StatusCode) : response.StatusCode switch
            {
                HttpStatusCode.Conflict => "This draft has changed. Reload the page before saving or publishing.",
                HttpStatusCode.BadRequest => "Check the fields: unique IDs, a title and a target from 1 to 10,000 enemies.",
                HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized => "Access to the catalogue was denied. Check the administration configuration.",
                _ => "The catalogue is unavailable. Try again shortly."
            };
            throw new ContentApiException(response.StatusCode, message);
        }
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(ct)
                ?? throw new ContentApiException(HttpStatusCode.BadGateway, publication ? "The publication service returned an empty response." : "The catalogue returned an empty response.");
        }
        catch (JsonException)
        {
            throw new ContentApiException(HttpStatusCode.BadGateway, publication ? "The publication service returned an invalid response." : "The catalogue returned an invalid response.");
        }
    }
}
