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
    public static string KindLabel(string kind) => kind == "release-note" ? "Release notes" : "News";
    public static string LanguageLabel(string language) => language == "en" ? "English" : "French";
    public static bool IsDependencyError(Exception ex, CancellationToken ct) => ex is ContentApiException or HttpRequestException
        || ex is TaskCanceledException && !ct.IsCancellationRequested;
    public static string ErrorMessage(Exception ex) => ex is ContentApiException ? ex.Message : ErrorMessage(HttpStatusCode.ServiceUnavailable);
    public static string ErrorMessage(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Conflict => "This publication has changed. Copy your changes, then reload the page before trying again.",
        HttpStatusCode.BadRequest => "Check the fields, language and game version for release notes.",
        HttpStatusCode.NotFound => "This publication no longer exists.",
        HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized => "Access to publications was denied. Check the administration configuration.",
        _ => "The publication service is unavailable. Try again shortly."
    };
}
