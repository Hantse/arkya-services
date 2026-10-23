using System.Net;

namespace Arkya.Admin.Content;

public sealed record ContentPostResponse(Guid Id, long Revision, string Kind, string Language, string Title,
    string Summary, string Body, string? GameVersion, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    DateTimeOffset? PublishedAt, long? PublishedRevision);
public sealed record ContentPostList(ContentPostResponse[] Items, bool HasMore);
public sealed record CreateContentPost(string Kind, string Language, string Title, string Summary, string Body, string? GameVersion);
public sealed record UpdateContentPost(long Revision, string Kind, string Language, string Title, string Summary, string Body, string? GameVersion);

public sealed partial class ContentAdminClient
{
    public Task<ContentPostList> GetPostsAsync(string? kind, string? language, int offset, int limit, CancellationToken ct)
    {
        var path = $"admin/content/posts?offset={offset}&limit={limit}";
        if (!string.IsNullOrEmpty(kind)) path += "&kind=" + Uri.EscapeDataString(kind);
        if (!string.IsNullOrEmpty(language)) path += "&language=" + Uri.EscapeDataString(language);
        return SendAsync<ContentPostList>(HttpMethod.Get, path, null, ct, publication: true);
    }

    public Task<ContentPostResponse> GetPostAsync(Guid id, CancellationToken ct) =>
        SendAsync<ContentPostResponse>(HttpMethod.Get, $"admin/content/posts/{id:D}", null, ct, publication: true);
    public Task<ContentPostResponse> CreatePostAsync(CreateContentPost post, CancellationToken ct) =>
        SendAsync<ContentPostResponse>(HttpMethod.Post, "admin/content/posts", post, ct, publication: true);
    public Task<ContentPostResponse> SavePostAsync(Guid id, UpdateContentPost post, CancellationToken ct) =>
        SendAsync<ContentPostResponse>(HttpMethod.Put, $"admin/content/posts/{id:D}", post, ct, publication: true);
    public Task<ContentPostResponse> PublishPostAsync(Guid id, long revision, CancellationToken ct) =>
        SendAsync<ContentPostResponse>(HttpMethod.Post, $"admin/content/posts/{id:D}/publish", new { revision }, ct, publication: true);
    public Task<ContentPostResponse> UnpublishPostAsync(Guid id, long revision, CancellationToken ct) =>
        SendAsync<ContentPostResponse>(HttpMethod.Post, $"admin/content/posts/{id:D}/unpublish", new { revision }, ct, publication: true);
}

public static class ContentPublicationPresentation
{
    public static bool IsKind(string? kind) => kind is "news" or "release-note";
    public static bool IsLanguage(string? language) => language is "fr" or "en";
    public static string KindLabel(string kind) => kind == "release-note" ? "Notes de version" : "Actualité";
    public static string LanguageLabel(string language) => language == "en" ? "Anglais" : "Français";
    public static bool IsDependencyError(Exception ex, CancellationToken ct) => ex is ContentApiException or HttpRequestException
        || ex is TaskCanceledException && !ct.IsCancellationRequested;
    public static string ErrorMessage(Exception ex) => ex is ContentApiException ? ex.Message : ErrorMessage(HttpStatusCode.ServiceUnavailable);
    public static string ErrorMessage(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Conflict => "Cette publication a changé. Copiez vos modifications, puis rechargez la page avant de réessayer.",
        HttpStatusCode.BadRequest => "Vérifiez les champs, la langue et la version du jeu pour les notes de version.",
        HttpStatusCode.NotFound => "Cette publication n'existe plus.",
        HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized => "L'accès aux publications a été refusé. Vérifiez la configuration de l'administration.",
        _ => "Le service de publications est indisponible. Réessayez dans un instant."
    };
}
