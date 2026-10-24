using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using ModelContextProtocol;

namespace Arkya.Publications.Mcp;

public sealed partial class PublicationsMcpClient(HttpClient client, IOptions<PublicationsMcpOptions> options)
{
    private const int MaximumResponseBytes = 8 * 1024 * 1024;
    private static readonly TimeSpan MaximumRequestDuration = TimeSpan.FromSeconds(20);

    public Task<PublicationListResponse> ListAsync(string? kind, string? language, int offset, int limit, CancellationToken ct)
    {
        if (offset is < 0 or > 10000 || limit is < 1 or > 50)
            throw new McpException("offset must be 0-10000 and limit 1-50.");
        if (kind is not null && !IsKind(kind) || language is not null && !IsLanguage(language))
            throw new McpException("Use kind news or release-note and language fr or en, or omit either filter.");
        var path = $"admin/content/posts?offset={offset.ToString(CultureInfo.InvariantCulture)}&limit={limit.ToString(CultureInfo.InvariantCulture)}";
        if (kind is not null) path += "&kind=" + Uri.EscapeDataString(kind);
        if (language is not null) path += "&language=" + Uri.EscapeDataString(language);
        return SendAsync<PublicationListResponse>(HttpMethod.Get, path, null, ct);
    }

    public Task<PublicationResponse> GetAsync(Guid id, CancellationToken ct) =>
        SendAsync<PublicationResponse>(HttpMethod.Get, PathFor(id), null, ct);

    public Task<PublicationResponse> CreateAsync(CreatePublicationRequest request, CancellationToken ct)
    {
        ValidateContent(request.Kind, request.Language, request.Title, request.Summary, request.Body, request.GameVersion);
        return SendAsync<PublicationResponse>(HttpMethod.Post, "admin/content/posts", request, ct);
    }

    public Task<PublicationResponse> UpdateAsync(Guid id, UpdatePublicationRequest request, CancellationToken ct)
    {
        ValidateRevision(request.Revision);
        ValidateContent(request.Kind, request.Language, request.Title, request.Summary, request.Body, request.GameVersion);
        return SendAsync<PublicationResponse>(HttpMethod.Put, PathFor(id), request, ct);
    }

    public Task<PublicationResponse> PublishAsync(Guid id, long expectedRevision, CancellationToken ct) =>
        ChangePublicationAsync(id, expectedRevision, "publish", ct);

    public Task<PublicationResponse> UnpublishAsync(Guid id, long expectedRevision, CancellationToken ct) =>
        ChangePublicationAsync(id, expectedRevision, "unpublish", ct);

    public Task<DeletePublicationResponse> DeleteAsync(Guid id, long expectedRevision, CancellationToken ct)
    {
        ValidateRevision(expectedRevision);
        var path = PathFor(id) + "?revision=" + expectedRevision.ToString(CultureInfo.InvariantCulture);
        return WithDeadlineAsync(async token =>
        {
            using var response = await SendRequestAsync(HttpMethod.Delete, path, null, token);
            if (response.StatusCode != HttpStatusCode.NoContent)
                throw new McpException("The publication service returned an unexpected deletion response.");
            return new DeletePublicationResponse(id, true);
        }, ct);
    }

    private Task<PublicationResponse> ChangePublicationAsync(Guid id, long expectedRevision, string action, CancellationToken ct)
    {
        ValidateRevision(expectedRevision);
        return SendAsync<PublicationResponse>(HttpMethod.Post, PathFor(id) + "/" + action, new { revision = expectedRevision }, ct);
    }

    private Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct) =>
        WithDeadlineAsync(async token =>
        {
            using var response = await SendRequestAsync(method, path, body, token);
            var bytes = await ReadBoundedAsync(response.Content, token);
            try
            {
                return JsonSerializer.Deserialize<T>(bytes, JsonSerializerOptions.Web)
                    ?? throw new McpException("The publication service returned an empty response.");
            }
            catch (JsonException) { throw new McpException("The publication service returned invalid JSON."); }
        }, ct);

    private async Task<T> WithDeadlineAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(client.Timeout > TimeSpan.Zero && client.Timeout < MaximumRequestDuration
            ? client.Timeout : MaximumRequestDuration);
        try { return await action(deadline.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new McpException("The publication service timed out."); }
        catch (HttpRequestException) { throw new McpException("Cannot read the publication service response."); }
        catch (IOException) { throw new McpException("Cannot read the publication service response."); }
    }

    private async Task<HttpResponseMessage> SendRequestAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Arkya-Admin-Key", options.Value.AdminKey);
        if (body is not null) request.Content = JsonContent.Create(body);
        HttpResponseMessage response;
        try { response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct); }
        catch (HttpRequestException) { throw new McpException("Cannot reach the configured publication service. Check its address, TLS and availability."); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new McpException("The publication service timed out."); }
        if (response.IsSuccessStatusCode) return response;
        var status = response.StatusCode;
        response.Dispose();
        throw new McpException(status switch
        {
            HttpStatusCode.Conflict => "The publication changed. Read it again and use its current revision; do not overwrite another operator's update.",
            HttpStatusCode.NotFound => "Publication not found.",
            HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized => "Publication administration access was refused. Check the operator configuration.",
            HttpStatusCode.BadRequest => "The publication service rejected the filters, content or expected revision.",
            HttpStatusCode.TooManyRequests => "Rate limit reached. Retry later.",
            _ => "The publication service is unavailable or returned an unexpected response."
        });
    }

    private static string PathFor(Guid id) => id == Guid.Empty
        ? throw new McpException("A non-empty publication ID is required.")
        : $"admin/content/posts/{id:D}";

    private static void ValidateRevision(long revision)
    {
        if (revision is < 1 or long.MaxValue)
            throw new McpException("Supply the current positive revision below Int64.MaxValue.");
    }

    private static void ValidateContent(string? kind, string? language, string? title, string? summary, string? body, string? gameVersion)
    {
        if (!IsKind(kind) || !IsLanguage(language))
            throw new McpException("Use kind news or release-note and language fr or en.");
        if (!IsValidText(title, 160, false))
            throw new McpException("Supply a non-blank title of at most 160 characters without control characters.");
        if (summary is not null && (summary.Length > 400 || summary.Any(char.IsControl)))
            throw new McpException("Supply a summary of at most 400 characters without control characters.");
        if (!IsValidText(body, 20000, true))
            throw new McpException("Supply a non-blank body of at most 20000 characters; only line breaks and tabs are allowed as control characters.");
        if (kind == "release-note" && (gameVersion is null || gameVersion.Length is < 1 or > 64 || !GameVersionPattern().IsMatch(gameVersion)))
            throw new McpException("Release notes require a version of 1-64 ASCII letters, digits, dots, underscores, plus signs or hyphens, starting with a letter or digit.");
        if (kind == "news" && !string.IsNullOrEmpty(gameVersion))
            throw new McpException("Only release notes may have a gameVersion.");
    }

    private static bool IsKind(string? kind) => kind is "news" or "release-note";
    private static bool IsLanguage(string? language) => language is "fr" or "en";
    private static bool IsValidText(string? text, int maximum, bool multiline) =>
        !string.IsNullOrWhiteSpace(text) && text.Length <= maximum
        && !text.Any(character => char.IsControl(character) && !(multiline && character is '\r' or '\n' or '\t'));

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, CancellationToken ct)
    {
        if (content.Headers.ContentLength > MaximumResponseBytes)
            throw new McpException("The publication service response exceeded the size limit.");
        await using var stream = await content.ReadAsStreamAsync(ct);
        using var result = new MemoryStream();
        var buffer = new byte[16384];
        int count;
        while ((count = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (result.Length + count > MaximumResponseBytes)
                throw new McpException("The publication service response exceeded the size limit.");
            result.Write(buffer, 0, count);
        }
        return result.ToArray();
    }

    [GeneratedRegex("\\A[A-Za-z0-9][A-Za-z0-9._+-]*\\z", RegexOptions.CultureInvariant, 100)]
    private static partial Regex GameVersionPattern();
}
