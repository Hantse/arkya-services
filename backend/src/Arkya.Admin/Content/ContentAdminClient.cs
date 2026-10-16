using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Arkya.Admin.Content;

public sealed class ContentApiOptions
{
    public string BaseUrl { get; init; } = "http://localhost:5143";
    public string AdminKey { get; init; } = "";

    public static bool IsSafe(string value, bool development) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment)
        && (uri.Scheme == "https" || development && uri.Scheme == "http" && uri.IsLoopback);
}

public sealed record QuestDefinition(string QuestId, string Title, string Description, string EnemyId, int RequiredKills);
public sealed record ContentDraft(long Revision, QuestDefinition[] Quests);
public sealed record ContentCatalog(int SchemaVersion, string Version, DateTimeOffset PublishedAt, QuestDefinition[] Quests);

public sealed class ContentApiException(HttpStatusCode statusCode, string message) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}

public sealed class ContentAdminClient(HttpClient client, IOptions<ContentApiOptions> options)
{
    public Task<ContentDraft> GetDraftAsync(CancellationToken ct) => SendAsync<ContentDraft>(HttpMethod.Get, "admin/content/draft", null, ct);
    public Task<ContentCatalog[]> GetCatalogsAsync(CancellationToken ct) => SendAsync<ContentCatalog[]>(HttpMethod.Get, "admin/content/catalogs", null, ct);
    public Task<ContentDraft> SaveAsync(ContentDraft draft, CancellationToken ct) => SendAsync<ContentDraft>(HttpMethod.Put, "admin/content/draft", draft, ct);
    public Task<ContentCatalog> PublishAsync(long revision, CancellationToken ct) => SendAsync<ContentCatalog>(HttpMethod.Post,
        "admin/content/catalogs", new { draftRevision = revision }, ct);

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Arkya-Admin-Key", options.Value.AdminKey);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var message = response.StatusCode switch
            {
                HttpStatusCode.Conflict => "Ce brouillon a changé. Rechargez la page avant d'enregistrer ou de publier.",
                HttpStatusCode.BadRequest => "Vérifiez les champs : identifiants uniques, titre renseigné et objectif entre 1 et 10 000 ennemis.",
                HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized => "L'accès au catalogue a été refusé. Vérifiez la configuration de l'administration.",
                _ => "Le catalogue est indisponible. Réessayez dans un instant."
            };
            throw new ContentApiException(response.StatusCode, message);
        }
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(ct)
                ?? throw new ContentApiException(HttpStatusCode.BadGateway, "Le catalogue a renvoyé une réponse vide.");
        }
        catch (JsonException)
        {
            throw new ContentApiException(HttpStatusCode.BadGateway, "Le catalogue a renvoyé une réponse invalide.");
        }
    }
}
