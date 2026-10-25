using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Arkya.Realms.Api.Application;
using Arkya.Realms.Api.Domain;
using Microsoft.Extensions.Options;

namespace Arkya.Realms.Api.Infrastructure;

public sealed class KubernetesNativeReleaseSource(HttpClient client, IOptions<DeploymentOptions> configured,
    IOptions<RealmNativeReleaseOptions> native)
{
    public async Task<NativePublishedRelease?> ReadAsync(CancellationToken cancellationToken)
    {
        var settings = native.Value;
        if (!settings.Enabled || !configured.Value.Enabled) return null;
        if (!settings.IsValid()) throw new InvalidOperationException("Native release source configuration is invalid.");
        var token = (await File.ReadAllTextAsync(configured.Value.TokenFile, cancellationToken)).Trim();
        if (token.Length is < 1 or > 32768 || token.Any(char.IsWhiteSpace)) throw new InvalidOperationException("Cluster credentials are invalid.");
        var path = $"api/v1/namespaces/{settings.Namespace}/configmaps/{settings.ConfigMapName}";
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(configured.Value.ApiServer.TrimEnd('/') + "/"), path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        await response.Content.LoadIntoBufferAsync(65536, cancellationToken);
        return Parse(await response.Content.ReadAsStringAsync(cancellationToken), settings.Namespace, settings.ConfigMapName);
    }

    public static NativePublishedRelease Parse(string json, string expectedNamespace = "arkya-beta",
        string expectedName = "arkya-native-runtime-release")
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
        var root = document.RootElement;
        static void Unique(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name)) throw new JsonException("Native release metadata contains duplicate keys.");
                    Unique(property.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array) foreach (var value in element.EnumerateArray()) Unique(value);
        }
        Unique(root);
        if (root.GetProperty("apiVersion").GetString() != "v1" || root.GetProperty("kind").GetString() != "ConfigMap" ||
            root.GetProperty("metadata").GetProperty("namespace").GetString() != expectedNamespace ||
            root.GetProperty("metadata").GetProperty("name").GetString() != expectedName) throw new JsonException("Native release source identity is invalid.");
        var data = root.GetProperty("data");
        string[] keys = ["schemaVersion", "channel", "adoption", "dedicatedImage", "sourceRevision", "protocolVersion",
            "buildId", "windowsClientSourceRevision", "buildVersion", "sourceRunId"];
        if (data.ValueKind != JsonValueKind.Object || !data.EnumerateObject().Select(value => value.Name).ToHashSet(StringComparer.Ordinal).SetEquals(keys) ||
            data.EnumerateObject().Any(value => value.Value.ValueKind != JsonValueKind.String)) throw new JsonException("Native release data schema is invalid.");
        string Field(string key) => data.GetProperty(key).GetString()!;
        if (Field("schemaVersion") != "1" || Field("channel") != "beta" || Field("adoption") != "manual" ||
            Field("sourceRevision") != Field("windowsClientSourceRevision") ||
            !int.TryParse(Field("protocolVersion"), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var version) || version.ToString(System.Globalization.CultureInfo.InvariantCulture) != Field("protocolVersion"))
            throw new JsonException("Native release provenance or channel is invalid.");
        var release = new NativePublishedRelease(Field("buildVersion"), Field("sourceRevision"), Field("sourceRunId"),
            Field("dedicatedImage"), version, Field("buildId"));
        if (!release.IsValid()) throw new JsonException("Native release identity or network contract is invalid.");
        return release;
    }
}

public sealed class RealmNativeReleaseWorker(KubernetesNativeReleaseSource source, RealmNativeReleaseCatalog catalog,
    IOptions<RealmNativeReleaseOptions> configured, ILogger<RealmNativeReleaseWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configured.Value.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                if (await source.ReadAsync(timeout.Token) is { } release && !catalog.Accept(release))
                    logger.LogWarning("Native release publication was rejected because its version conflicts with the accepted catalogue.");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException or
                InvalidOperationException or KeyNotFoundException or OperationCanceledException)
            {
                logger.LogWarning("Native release publication could not be read; existing approvals remain pinned.");
            }
            try { await Task.Delay(TimeSpan.FromSeconds(configured.Value.PollIntervalSeconds), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }
}
