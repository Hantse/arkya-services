using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace Arkya.Launcher.Core;

public sealed class ReleaseClient(HttpClient httpClient)
{
    public static HttpClient CreateHttpClient() => new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.None
    }) { Timeout = Timeout.InfiniteTimeSpan };

    public async Task<GameManifest> GetManifestAsync(string address, CancellationToken cancellationToken)
    {
        var bytes = await ReadManifestAsync(address, cancellationToken);
        return ParseManifest(bytes);
    }

    public async Task<GameManifest?> GetChannelReleaseAsync(ReleaseChannel channel, CancellationToken cancellationToken)
    {
        var bytes = await ReadManifestAsync(ReleaseChannels.GetManifestUrl(channel), cancellationToken);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Manifeste de canal invalide.");
        if (root.TryGetProperty("channel", out var declaredChannel) &&
            (declaredChannel.ValueKind != JsonValueKind.String || declaredChannel.GetString() != ReleaseChannels.GetId(channel)))
            throw new InvalidDataException("Le manifeste ne correspond pas au canal sélectionné.");
        if (root.TryGetProperty("available", out var available))
        {
            if (available.ValueKind != JsonValueKind.False ||
                !root.TryGetProperty("schemaVersion", out var schema) || schema.ValueKind != JsonValueKind.Number ||
                !schema.TryGetInt32(out var version) || version != 1 ||
                !root.TryGetProperty("channel", out _) || root.EnumerateObject().Count() != 3)
                throw new InvalidDataException("État de publication du canal invalide.");
            return null;
        }
        return ParseManifest(bytes);
    }

    private async Task<byte[]> ReadManifestAsync(string address, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await GetAsync(GameManifest.ValidateHttpsUrl(address), timeout.Token);
        if (response.Content.Headers.ContentLength > 65536)
            throw new InvalidDataException("Le manifeste de version est trop volumineux.");
        await using var source = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var destination = new MemoryStream();
        await CopyBoundedAsync(source, destination, 65536, timeout.Token);
        return destination.ToArray();
    }

    private static GameManifest ParseManifest(byte[] bytes)
    {
        var manifest = JsonSerializer.Deserialize<GameManifest>(bytes, GameManifest.JsonOptions)
            ?? throw new InvalidDataException("Manifeste de version vide.");
        manifest.Validate();
        return manifest;
    }

    public async Task DownloadAsync(GameManifest manifest, string destination, IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        manifest.Validate();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromHours(6));
        using var response = await GetAsync(GameManifest.ValidateHttpsUrl(manifest.PackageUrl), timeout.Token);
        if (response.Content.Headers.ContentLength is long length && length != manifest.PackageSize)
            throw new InvalidDataException("La taille du téléchargement ne correspond pas à la version annoncée.");
        await using var source = await response.Content.ReadAsStreamAsync(timeout.Token);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[131072];
        long total = 0;
        while (true)
        {
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            idle.CancelAfter(TimeSpan.FromSeconds(60));
            var read = await source.ReadAsync(buffer, idle.Token);
            if (read == 0) break;
            total += read;
            if (total > manifest.PackageSize)
                throw new InvalidDataException("Le téléchargement dépasse la taille attendue.");
            hash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
            progress?.Report(new("Téléchargement du jeu", total, manifest.PackageSize));
        }
        if (total != manifest.PackageSize || !CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), Convert.FromHexString(manifest.Sha256)))
            throw new InvalidDataException("Le fichier téléchargé est incomplet ou altéré. Réessayez le téléchargement.");
        await output.FlushAsync(timeout.Token);
    }

    private async Task<HttpResponseMessage> GetAsync(Uri address, CancellationToken cancellationToken)
    {
        for (var redirect = 0; redirect <= 5; redirect++)
        {
            using var headerTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            headerTimeout.CancelAfter(TimeSpan.FromSeconds(30));
            var response = await httpClient.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, headerTimeout.Token);
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                var next = response.Headers.Location;
                response.Dispose();
                if (next is null) throw new HttpRequestException("Redirection de téléchargement invalide.");
                address = GameManifest.ValidateHttpsUrl(new Uri(address, next).AbsoluteUri);
                continue;
            }
            try
            {
                response.EnsureSuccessStatusCode();
                if (response.RequestMessage?.RequestUri is { } actual)
                    GameManifest.ValidateHttpsUrl(actual.AbsoluteUri);
                return response;
            }
            catch { response.Dispose(); throw; }
        }
        throw new HttpRequestException("Trop de redirections de téléchargement.");
    }

    internal static async Task<long> CopyBoundedAsync(Stream source, Stream destination, long limit, CancellationToken cancellationToken)
    {
        var buffer = new byte[131072];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > limit) throw new InvalidDataException("Le fichier dépasse la taille autorisée.");
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return total;
    }
}
