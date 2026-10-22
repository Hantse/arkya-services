using System.Text.Json;

namespace Arkya.Launcher.Core;

public sealed record ContentPost(string Id, string Kind, string Language, string Title, string Summary,
    string Body, string? GameVersion, DateTimeOffset PublishedAt);

public sealed class ContentFeedClient(HttpClient httpClient)
{
    public async Task<IReadOnlyList<ContentPost>> GetPostsAsync(string address, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var response = await httpClient.GetAsync(GameManifest.ValidateHttpsUrl(address),
            HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 2 * 1024 * 1024)
            throw new InvalidDataException("Le flux de publications est trop volumineux.");
        await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var data = new MemoryStream();
        await ReleaseClient.CopyBoundedAsync(input, data, 2 * 1024 * 1024, timeout.Token);
        var feed = JsonSerializer.Deserialize<ContentFeed>(data.ToArray(), GameManifest.JsonOptions)
            ?? throw new InvalidDataException("Flux de publications vide.");
        if (feed.Items is null || feed.Items.Length > 50)
            throw new InvalidDataException("Nombre de publications invalide.");
        foreach (var item in feed.Items)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.Id) || item.Id.Length > 100 ||
                item.Kind is not ("news" or "release-note") || item.Language is not ("fr" or "en") ||
                !ValidText(item.Title, 160, true) || !ValidText(item.Summary, 400, false) ||
                !ValidText(item.Body, 20000, true) || item.PublishedAt == default ||
                (item.Kind == "release-note" && !ValidText(item.GameVersion, 64, true)))
                throw new InvalidDataException("Une publication est invalide.");
        }
        return feed.Items;
    }

    private static bool ValidText(string? text, int maximum, bool required) => text is not null &&
        text.Length <= maximum && (!required || !string.IsNullOrWhiteSpace(text));

    private sealed record ContentFeed(ContentPost[] Items, bool HasMore);
}
