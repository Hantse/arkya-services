using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Arkya.Admin.Content;

public sealed class ItemVisualExportOptions
{
    public bool Enabled { get; init; }
}

public sealed record ItemVisualRegistration(
    [property: JsonRequired] string? Name,
    [property: JsonRequired] string? AssetPath,
    [property: JsonRequired] string? Kind,
    [property: JsonRequired] string? DependencyHash,
    string? ThumbnailPngBase64);

public static class ItemVisualExport
{
    public const string PathPrefix = "/admin/content/item-assets";
    public const int MaximumRequestBytes = 512 * 1024;
    public const int RequestsPerMinute = 120;
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8
    };

    public static bool IsPath(PathString path) => path.StartsWithSegments(PathPrefix, StringComparison.OrdinalIgnoreCase);

    public static IServiceCollection AddItemVisualExport(this IServiceCollection services) => services
        .AddOptions<ItemVisualExportOptions>().BindConfiguration("ItemVisualExport")
        .Validate<IOptions<ContentApiOptions>>((export, content) => !export.Enabled || content.Value.Enabled,
            "ItemVisualExport requires ContentApi:Enabled.").ValidateOnStart().Services;

    public static IApplicationBuilder UseItemVisualExport(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        if (!IsPath(context.Request.Path)) { await next(); return; }
        // This machine endpoint never authenticates or renews browser sessions.
        await HandleAsync(context);
    });

    private static async Task HandleAsync(HttpContext context)
    {
        var request = context.Request;
        var enabled = context.RequestServices.GetRequiredService<IOptions<ItemVisualExportOptions>>().Value.Enabled;
        var suffix = request.Path.Value![PathPrefix.Length..];
        if (!enabled || suffix.Length != 33 || suffix[0] != '/' || !IsUnityHash(suffix[1..]))
        { context.Response.StatusCode = StatusCodes.Status404NotFound; return; }
        if (!HttpMethods.IsPut(request.Method))
        {
            context.Response.Headers.Allow = "PUT";
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            return;
        }
        if (!request.IsHttps || request.Headers.ContainsKey("Origin"))
        { await ErrorAsync(context, 403, "HTTPS machine requests without an Origin header are required."); return; }
        var expected = context.RequestServices.GetRequiredService<IOptions<ContentApiOptions>>().Value.AdminKey;
        var keys = request.Headers["X-Arkya-Admin-Key"];
        if (keys.Count != 1 || keys[0] is not { Length: >= 32 and <= 512 } key || key.Any(char.IsControl) ||
            !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(key)),
                SHA256.HashData(Encoding.UTF8.GetBytes(expected))))
        { await ErrorAsync(context, 401, "Item visual export authentication failed."); return; }
        if (request.ContentLength > MaximumRequestBytes)
        { await ErrorAsync(context, 413, "The visual registration exceeds the request limit."); return; }
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var media) ||
            !string.Equals(media.MediaType, "application/json", StringComparison.OrdinalIgnoreCase) ||
            media.CharSet is { Length: > 0 } charset && !string.Equals(charset.Trim('"'), "utf-8", StringComparison.OrdinalIgnoreCase))
        { await ErrorAsync(context, 415, "Use application/json with UTF-8 encoding."); return; }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            var bytes = await ReadBoundedAsync(request.Body, MaximumRequestBytes, timeout.Token);
            if (bytes is null)
            { await ErrorAsync(context, 413, "The visual registration exceeds the request limit."); return; }
            var registration = JsonSerializer.Deserialize<ItemVisualRegistration>(bytes, JsonOptions);
            if (registration is null)
            { await ErrorAsync(context, 400, "Provide a valid visual registration."); return; }
            var client = context.RequestServices.GetRequiredService<ContentAdminClient>();
            var asset = await client.UploadItemVisualAsync(suffix[1..], registration, timeout.Token);
            await context.Response.WriteAsJsonAsync(asset, timeout.Token);
        }
        catch (JsonException) { await ErrorAsync(context, 400, "Provide a valid visual registration."); }
        catch (ContentApiException ex)
        {
            var invalid = ex.StatusCode == HttpStatusCode.BadRequest;
            await ErrorAsync(context, invalid ? 400 : 502, invalid
                ? "The visual registration is invalid. Check the model metadata and PNG preview."
                : "The content service could not register this visual. Try again shortly.");
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (OperationCanceledException) { await ErrorAsync(context, 504, "The visual registration timed out. Try again shortly."); }
        catch (HttpRequestException) { await ErrorAsync(context, 502, "The content service is unavailable. Try again shortly."); }
        catch (IOException) { await ErrorAsync(context, 400, "The visual registration could not be read."); }
    }

    internal static async Task<byte[]?> ReadBoundedAsync(Stream stream, int limit, CancellationToken ct)
    {
        using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, limit + 1 - (int)bytes.Length)), ct);
            if (count == 0) return bytes.ToArray();
            if (bytes.Length + count > limit) return null;
            bytes.Write(buffer, 0, count);
        }
    }

    internal static bool IsUnityHash(string? value) => ContentItemPresentation.IsAssetGuid(value) && value!.Any(c => c != '0');
    private static Task ErrorAsync(HttpContext context, int status, string message)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new { error = message }, context.RequestAborted);
    }
}

public sealed partial class ContentAdminClient
{
    public async Task<ItemVisualAsset> UploadItemVisualAsync(string guid, ItemVisualRegistration registration, CancellationToken ct)
    {
        if (!Enabled) throw new ContentApiException(HttpStatusCode.ServiceUnavailable, "Item management is disabled.");
        using var request = new HttpRequestMessage(HttpMethod.Put, "admin/content/item-assets/" + guid)
        { Content = JsonContent.Create(registration) };
        request.Headers.Add("X-Arkya-Admin-Key", options.Value.AdminKey);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
            throw new ContentApiException(response.StatusCode, "The content service could not register this visual.");
        if (response.Content.Headers.ContentType?.MediaType != "application/json" || response.Content.Headers.ContentLength is > 16384)
            throw InvalidVisualResponse();
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var bytes = await ItemVisualExport.ReadBoundedAsync(stream, 16384, ct);
            if (bytes is null) throw InvalidVisualResponse();
            var asset = JsonSerializer.Deserialize<ItemVisualAsset>(bytes, ItemVisualExport.JsonOptions);
            if (asset is null || asset.Guid != guid || asset.Name is not { Length: > 0 and <= 120 } ||
                asset.AssetPath is not { Length: > 0 and <= 512 } || asset.Kind is not ("prefab" or "fbx" or "obj") ||
                !ItemVisualExport.IsUnityHash(asset.DependencyHash) || asset.UpdatedAt == default)
                throw InvalidVisualResponse();
            return asset;
        }
        catch (JsonException) { throw InvalidVisualResponse(); }
        catch (IOException) { throw InvalidVisualResponse(); }
    }

    private static ContentApiException InvalidVisualResponse() => new(HttpStatusCode.BadGateway, "The content service returned an invalid visual response.");
}
