using System.Security.Cryptography;
using System.Text.Json;

namespace Arkya.Launcher.Core;

public sealed class LauncherUpdateClient(HttpClient httpClient)
{
    public const string ManifestUrl = "https://arkya.online/downloads/launcher/latest-win-x64.json";

    public async Task<LauncherUpdateManifest?> GetUpdateAsync(Version currentVersion, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await GetAsync(new(ManifestUrl), true, timeout.Token);
        if (response.Content.Headers.ContentLength > 16384)
            throw new InvalidDataException("Le manifeste du launcher est trop volumineux.");
        await using var source = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var buffer = new MemoryStream();
        await ReleaseClient.CopyBoundedAsync(source, buffer, 16384, timeout.Token);
        var manifest = JsonSerializer.Deserialize<LauncherUpdateManifest>(buffer.ToArray(), GameManifest.JsonOptions)
            ?? throw new InvalidDataException("Manifeste du launcher vide.");
        manifest.Validate();
        var current = new Version(currentVersion.Major, currentVersion.Minor,
            Math.Max(0, currentVersion.Build), Math.Max(0, currentVersion.Revision));
        return manifest.GetVersion() > current ? manifest : null;
    }

    public async Task DownloadAsync(LauncherUpdateManifest manifest, string destination,
        IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
    {
        manifest.Validate();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(15));
        var createdOutput = false;
        try
        {
            using var response = await GetAsync(new(manifest.InstallerUrl), false, timeout.Token);
            if (response.Content.Headers.ContentLength is long size && size != manifest.InstallerSize)
                throw new InvalidDataException("La taille de l’installateur ne correspond pas à la publication.");
            await using var source = await response.Content.ReadAsStreamAsync(timeout.Token);
            await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 131072, true);
            createdOutput = true;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[131072];
            long total = 0;
            while (true)
            {
                using var idle = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
                idle.CancelAfter(TimeSpan.FromSeconds(60));
                var count = await source.ReadAsync(buffer, idle.Token);
                if (count == 0) break;
                total += count;
                if (total > manifest.InstallerSize)
                    throw new InvalidDataException("Le téléchargement dépasse la taille de l’installateur.");
                hash.AppendData(buffer, 0, count);
                await output.WriteAsync(buffer.AsMemory(0, count), timeout.Token);
                progress?.Report(new("Mise à jour du launcher", total, manifest.InstallerSize));
            }
            if (total != manifest.InstallerSize || !CryptographicOperations.FixedTimeEquals(
                    hash.GetHashAndReset(), Convert.FromHexString(manifest.Sha256)))
                throw new InvalidDataException("L’installateur téléchargé est incomplet ou altéré.");
            await output.FlushAsync(timeout.Token);
        }
        catch
        {
            // Keep previous downloads and user files intact; this operation owns only its newly created file.
            if (createdOutput && File.Exists(destination)) File.Delete(destination);
            throw;
        }
    }

    private async Task<HttpResponseMessage> GetAsync(Uri address, bool manifest, CancellationToken cancellationToken)
    {
        for (var redirect = 0; redirect <= 5; redirect++)
        {
            ValidateAddress(address, manifest);
            using var headerTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            headerTimeout.CancelAfter(TimeSpan.FromSeconds(30));
            using var request = new HttpRequestMessage(HttpMethod.Get, address);
            request.Headers.CacheControl = new() { NoCache = true };
            var raw = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headerTimeout.Token);
            if ((int)raw.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                var next = raw.Headers.Location;
                raw.Dispose();
                if (next is null)
                    throw new HttpRequestException("Redirection de mise à jour invalide.");
                address = new Uri(address, next);
                continue;
            }
            try
            {
                raw.EnsureSuccessStatusCode();
                if (raw.RequestMessage?.RequestUri is { } actual) ValidateAddress(actual, manifest);
                return raw;
            }
            catch { raw.Dispose(); throw; }
        }
        throw new HttpRequestException("Trop de redirections de mise à jour du launcher.");
    }

    private static void ValidateAddress(Uri address, bool manifest)
    {
        if (manifest)
        {
            if (address.AbsoluteUri != ManifestUrl)
                throw new InvalidDataException("Adresse du manifeste du launcher non autorisée.");
        }
        else LauncherUpdateManifest.ValidateDownloadUrl(address.AbsoluteUri);
    }
}
