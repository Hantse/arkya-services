using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arkya.Realms.Api.Application;
using Arkya.Realms.Api.Domain;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Options;

namespace Arkya.Realms.Api.Infrastructure;

public sealed partial class KubernetesRealmDeployer(HttpClient client, IOptions<DeploymentOptions> configured,
    IOptions<RealmRuntimeOptions>? runtime = null) : IRealmDeployer
{
    private const string DedicatedSecretName = "arkya-dedicated";
    private const string InferenceSecretName = "arkya-npc-dialogue";
    private const string ImageReleaseAnnotation = "arkya.online/image-release";
    private const string RestartAnnotation = "arkya.online/restart-revision";
    private readonly DeploymentOptions options = configured.Value;
    private readonly RealmRuntimeOptions runtimeOptions = runtime?.Value ?? new();

    public async Task<bool> DeleteAsync(RealmRecord realm, CancellationToken cancellationToken)
    {
        if (!RealmTopologyBuilder.SnapshotSupportsRuntime(realm.Profile))
            throw new DeploymentFailure("topology_runtime_unsupported");
        if (!options.Enabled || !options.Targets.Any(target => target.Id == realm.TargetId && target.Namespace == realm.Namespace &&
            (target.RealmId is null || target.RealmId == realm.RealmId)))
            throw new DeploymentFailure("target_unavailable");
        var resources = new List<(string Path, JsonObject Desired, JsonObject? Existing)>();
        var profiles = new[] { realm.Profile }.Concat(realm.RuntimeRelease is { } transition ? [transition.PreviousProfile] : []);
        var servers = profiles.SelectMany(profile => profile.Servers.Select(server => (Profile: profile, Server: server)))
            .DistinctBy(value => value.Server.Id);
        foreach (var (profile, server) in servers)
        {
            // Legacy workloads retain the labels of the profile that created them.
            var owner = realm with { Profile = profile };
            var desired = new JsonObject
            {
                ["metadata"] = new JsonObject
                {
                    ["name"] = ResourceName(owner, server), ["namespace"] = realm.Namespace, ["labels"] = Labels(owner, server)
                }
            };
            foreach (var collection in new[] { "apis/apps/v1", "api/v1" })
            {
                var kind = collection == "api/v1" ? "services" : "deployments";
                var path = $"{collection}/namespaces/{realm.Namespace}/{kind}/{ResourceName(realm, server)}";
                var existing = await GetAsync(path, cancellationToken);
                if (existing is not null) ValidateDeletionOwnership(desired, existing);
                resources.Add((path, desired, existing));
            }
        }

        if (!await DeleteResourcesAsync(resources, cancellationToken)) return false;

        // Runtime credentials must outlive draining dedicateds and belong to this deployment before cleanup.
        var secrets = new List<(string Path, JsonObject Desired, JsonObject? Existing)>();
        foreach (var name in new[] { DedicatedSecretName, "arkya-ghcr" }
            .Concat(runtimeOptions.InferenceBaseUrl.Length > 0 ? [InferenceSecretName] : []))
        {
            var desired = new JsonObject
            {
                ["metadata"] = new JsonObject
                {
                    ["name"] = name, ["namespace"] = realm.Namespace,
                    ["labels"] = new JsonObject
                    {
                        ["app.kubernetes.io/managed-by"] = "arkya-realms",
                        ["arkya.online/realm-id"] = realm.RealmId,
                        ["arkya.online/deployment-id"] = realm.DeploymentIdentity
                    }
                }
            };
            var path = $"api/v1/namespaces/{realm.Namespace}/secrets/{name}";
            var existing = await GetAsync(path, cancellationToken);
            if (existing is null || !MatchesOwnership(desired, existing)) continue;
            ValidateDeletionOwnership(desired, existing);
            secrets.Add((path, desired, existing));
        }
        return await DeleteResourcesAsync(secrets, cancellationToken);
    }

    private async Task<bool> DeleteResourcesAsync(List<(string Path, JsonObject Desired, JsonObject? Existing)> resources,
        CancellationToken cancellationToken)
    {
        // Validate the entire plan before deleting anything, including a Service discovered after its Deployment.
        foreach (var resource in resources)
        {
            if (resource.Existing is null || resource.Existing["metadata"]!["deletionTimestamp"] is not null) continue;
            var body = new JsonObject
            {
                ["apiVersion"] = "v1", ["kind"] = "DeleteOptions", ["propagationPolicy"] = "Foreground",
                ["preconditions"] = new JsonObject
                {
                    ["uid"] = resource.Existing["metadata"]!["uid"]!.DeepClone(),
                    ["resourceVersion"] = resource.Existing["metadata"]!["resourceVersion"]!.DeepClone()
                }
            };
            using var response = await SendAsync(HttpMethod.Delete, resource.Path, body, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound) continue;
            if (response.StatusCode == HttpStatusCode.Conflict) throw new DeploymentFailure("resource_conflict");
            RequireSuccess(response);
        }

        var complete = true;
        foreach (var resource in resources)
        {
            var existing = await GetAsync(resource.Path, cancellationToken);
            if (existing is null) continue;
            ValidateDeletionOwnership(resource.Desired, existing);
            complete = false;
        }
        return complete;
    }

    private static void ValidateDeletionOwnership(JsonObject desired, JsonObject existing)
    {
        if (!MatchesOwnership(desired, existing)) throw new DeploymentFailure("resource_ownership_conflict");
        var metadata = existing["metadata"]!.AsObject();
        if (metadata["annotations"] is JsonObject annotations && annotations.Any(pair =>
                pair.Key.StartsWith("argocd.argoproj.io/", StringComparison.Ordinal) ||
                pair.Key.StartsWith("meta.helm.sh/", StringComparison.Ordinal)) ||
            metadata["labels"] is JsonObject labels && labels.Any(pair =>
                pair.Key is "app.kubernetes.io/instance" or "argocd.argoproj.io/instance" ||
                pair.Key.StartsWith("kustomize.toolkit.fluxcd.io/", StringComparison.Ordinal) ||
                pair.Key.StartsWith("helm.toolkit.fluxcd.io/", StringComparison.Ordinal)) ||
            metadata["managedFields"] is JsonArray managers && managers.Any(field =>
                field?["manager"] is JsonValue manager && manager.TryGetValue<string>(out var name) &&
                name is "argocd-controller" or "argocd-application-controller" or "kustomize-controller" or "helm-controller") ||
            metadata["ownerReferences"] is JsonArray { Count: > 0 })
            throw new DeploymentFailure("resource_protected");
        if (metadata["uid"] is not JsonValue uid || !uid.TryGetValue<string>(out var uidValue) || string.IsNullOrWhiteSpace(uidValue) ||
            metadata["resourceVersion"] is not JsonValue version || !version.TryGetValue<string>(out var versionValue) || string.IsNullOrWhiteSpace(versionValue))
            throw new DeploymentFailure("cluster_response_invalid");
    }

    public async Task<ServerObservation[]> ReconcileAsync(RealmRecord realm, CancellationToken cancellationToken)
    {
        if (!RealmTopologyBuilder.SnapshotSupportsRuntime(realm.Profile))
            throw new DeploymentFailure("topology_runtime_unsupported");
        if (!options.Enabled || !options.Targets.Any(target => target.Id == realm.TargetId && target.Namespace == realm.Namespace &&
            (target.RealmId is null || target.RealmId == realm.RealmId)))
            throw new DeploymentFailure("target_unavailable");
        if (realm.ImageRelease is not null && !RealmImageReleases.Authorizes(options, realm))
            throw new DeploymentFailure("image_release_conflict");
        if (realm.RuntimeRelease is { Completed: false } && !await RetirePreviousRuntimeAsync(realm, cancellationToken))
            return [];
        if (realm.Restart is { } restart && (restart.Revision <= 0 || restart.PreviousRevision < 0 ||
            restart.PreviousRevision >= restart.Revision || restart.Revision > realm.Revision))
            throw new DeploymentFailure("restart_conflict");
        if (runtimeOptions.RegistryDockerConfigFile.Length > 0)
            await EnsureRegistrySecretAsync(realm, cancellationToken);
        if (runtimeOptions.DedicatedKeySeed.Length > 0)
            await EnsureDedicatedSecretAsync(realm, cancellationToken);
        if (runtimeOptions.InferenceBaseUrl.Length > 0)
        {
            if (!runtimeOptions.IsValid()) throw new DeploymentFailure("runtime_configuration_invalid");
            await EnsureOwnedSecretAsync(realm, InferenceSecretName, "Opaque", new JsonObject
                { ["service-key"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(runtimeOptions.InferenceServiceKey)) },
                "runtime_configuration_invalid", cancellationToken);
        }
        var results = new List<ServerObservation>();
        foreach (var server in realm.Profile.Servers)
        {
            var deployment = await EnsureAsync($"apis/apps/v1/namespaces/{realm.Namespace}/deployments",
                DeploymentManifest(realm, server, runtimeOptions), cancellationToken, realm);
            var service = await EnsureAsync($"api/v1/namespaces/{realm.Namespace}/services",
                ServiceManifest(realm, server), cancellationToken);
            if (deployment["status"]?["conditions"] is JsonArray conditions && conditions.Any(condition =>
                    condition?["type"]?.GetValue<string>() == "Progressing" && condition?["status"]?.GetValue<string>() == "False"))
                throw new DeploymentFailure("deployment_progress_failed");
            var generation = deployment["metadata"]?["generation"]?.GetValue<long>() ?? 1;
            var observed = deployment["status"]?["observedGeneration"]?.GetValue<long>() ?? 0;
            var available = deployment["status"]?["availableReplicas"]?.GetValue<int>() ?? 0;
            var updated = deployment["status"]?["updatedReplicas"]?.GetValue<int>() ?? 0;
            var ready = observed >= generation && available == 1 && updated == 1 ? 1 : 0;
            var ingress = (service["status"]?["loadBalancer"]?["ingress"] as JsonArray)?.FirstOrDefault();
            var address = ingress?["ip"]?.GetValue<string>() ?? ingress?["hostname"]?.GetValue<string>();
            if (server.Transport == "WebSocket") address = server.PublicEndpoint;
            if (address is { Length: > 253 } || address?.Any(char.IsControl) == true) address = null;
            results.Add(new(server.Id, ready, address)
            {
                ReadyImage = ready == 1 ? realm.Profile.Image : null,
                ReadyRestartRevision = ready == 1 ? realm.Restart?.Revision : null
            });
        }
        return results.ToArray();
    }

    private async Task EnsureRegistrySecretAsync(RealmRecord realm, CancellationToken cancellationToken)
    {
        var name = realm.Profile.ImagePullSecretName;
        if (name != "arkya-ghcr") throw new DeploymentFailure("runtime_configuration_invalid");
        var dockerConfig = await ReadRegistryCredentialsAsync(cancellationToken);
        await EnsureOwnedSecretAsync(realm, name, "kubernetes.io/dockerconfigjson",
            new JsonObject { [".dockerconfigjson"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(dockerConfig)) },
            "registry_credentials_invalid", cancellationToken);
    }

    private Task EnsureDedicatedSecretAsync(RealmRecord realm, CancellationToken cancellationToken)
    {
        if (!runtimeOptions.IsValid()) throw new DeploymentFailure("runtime_configuration_invalid");
        var data = new JsonObject();
        foreach (var server in realm.Profile.Servers)
            data[server.Id] = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                RealmRuntimeKeys.DeriveDedicatedKey(runtimeOptions.DedicatedKeySeed, realm.RealmId, server.Id)));
        return EnsureOwnedSecretAsync(realm, DedicatedSecretName, "Opaque", data,
            "runtime_configuration_invalid", cancellationToken);
    }

    private async Task EnsureOwnedSecretAsync(RealmRecord realm, string name, string type, JsonObject data,
        string invalidResponseCode, CancellationToken cancellationToken)
    {
        var desired = new JsonObject
        {
            ["apiVersion"] = "v1", ["kind"] = "Secret",
            ["metadata"] = new JsonObject
            {
                ["name"] = name, ["namespace"] = realm.Namespace,
                ["labels"] = new JsonObject
                {
                    ["app.kubernetes.io/managed-by"] = "arkya-realms",
                    ["arkya.online/realm-id"] = realm.RealmId,
                    ["arkya.online/deployment-id"] = realm.DeploymentIdentity
                }
            },
            ["type"] = type,
            ["data"] = data
        };
        var collection = $"api/v1/namespaces/{realm.Namespace}/secrets";
        var resource = $"{collection}/{name}";
        JsonObject? existing;
        try
        {
            existing = await GetAsync(resource, cancellationToken);
            if (existing is null)
            {
                using var response = await SendAsync(HttpMethod.Post, collection, desired, cancellationToken);
                if (response.StatusCode == HttpStatusCode.Conflict)
                    existing = await GetAsync(resource, cancellationToken) ?? throw new DeploymentFailure("resource_conflict");
                else
                {
                    RequireSuccess(response);
                    existing = await ReadAsync(response, cancellationToken);
                }
            }
        }
        catch (JsonException) { throw new DeploymentFailure(invalidResponseCode); }
        if (existing["metadata"] is not JsonObject metadata)
            throw new DeploymentFailure(invalidResponseCode);
        if (metadata["deletionTimestamp"] is not null)
            throw new DeploymentFailure("resource_deleting");
        if (!ContainsDesired(desired["metadata"]!["labels"], metadata["labels"]))
            throw new DeploymentFailure("resource_ownership_conflict");
        if (name == DedicatedSecretName && !JsonNode.DeepEquals(desired["data"], existing["data"]))
            existing = await MigrateDedicatedSecretAsync(realm, resource, desired, existing, cancellationToken);
        if (!JsonNode.DeepEquals(desired["type"], existing["type"]) || !JsonNode.DeepEquals(desired["data"], existing["data"]))
            throw new DeploymentFailure("resource_configuration_drift");
    }

    private async Task<string> ReadRegistryCredentialsAsync(CancellationToken cancellationToken)
    {
        const int limit = 65536;
        try
        {
            await using var stream = new FileStream(runtimeOptions.RegistryDockerConfigFile, FileMode.Open, FileAccess.Read,
                FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length > limit) throw new DeploymentFailure("registry_credentials_invalid");
            using var contents = new MemoryStream();
            var chunk = new byte[4096];
            int count;
            while ((count = await stream.ReadAsync(chunk.AsMemory(), cancellationToken)) > 0)
            {
                if (contents.Length + count > limit) throw new DeploymentFailure("registry_credentials_invalid");
                contents.Write(chunk, 0, count);
            }
            var root = JsonNode.Parse(contents.GetBuffer().AsSpan(0, (int)contents.Length), documentOptions:
                new JsonDocumentOptions { MaxDepth = 16 });
            if (root is not JsonObject document || document["auths"] is not JsonObject auths ||
                auths["ghcr.io"] is not JsonObject credentials || !HasRegistryCredentials(credentials))
                throw new DeploymentFailure("registry_credentials_invalid");
            return new JsonObject { ["auths"] = new JsonObject { ["ghcr.io"] = credentials.DeepClone() } }.ToJsonString();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new DeploymentFailure("registry_credentials_unreadable");
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
        {
            throw new DeploymentFailure("registry_credentials_invalid");
        }
    }

    private static bool HasRegistryCredentials(JsonObject credentials) => credentials["auth"] is JsonValue property &&
        property.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) && !text.Any(char.IsControl);

    private async Task<JsonObject> EnsureAsync(string collection, JsonObject desired, CancellationToken cancellationToken,
        RealmRecord? realm = null)
    {
        var resource = $"{collection}/{desired["metadata"]!["name"]!.GetValue<string>()}";
        var existing = await GetAsync(resource, cancellationToken);
        if (existing is null)
        {
            using var response = await SendAsync(HttpMethod.Post, collection, desired, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Conflict)
                existing = await GetAsync(resource, cancellationToken) ?? throw new DeploymentFailure("resource_conflict");
            else
            {
                RequireSuccess(response);
                existing = await ReadAsync(response, cancellationToken);
            }
        }
        if (existing["metadata"]?["deletionTimestamp"] is not null)
            throw new DeploymentFailure("resource_deleting");
        if (!MatchesOwnership(desired, existing))
            throw new DeploymentFailure("resource_ownership_conflict");
        var configuration = desired;
        var restartRequired = false;
        if (realm?.Restart is { } restart)
        {
            var actualRevision = RestartRevision(existing);
            if (actualRevision != restart.Revision && actualRevision != restart.PreviousRevision)
                throw new DeploymentFailure("restart_conflict");
            restartRequired = actualRevision != restart.Revision;
            if (restartRequired)
            {
                configuration = desired.DeepClone().AsObject();
                var annotations = configuration["spec"]!["template"]!["metadata"]!["annotations"]!.AsObject();
                if (restart.PreviousRevision == 0) configuration["spec"]!["template"]!["metadata"]!.AsObject().Remove("annotations");
                else annotations[RestartAnnotation] = restart.PreviousRevision.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }
        var activateInference = false;
        if (restartRequired && realm?.Restart is { Completed: false } && runtimeOptions.InferenceBaseUrl.Length > 0 &&
            InferenceDisabledConfiguration(configuration, existing) is { } inferenceDisabled)
        {
            configuration = inferenceDisabled;
            activateInference = true;
        }
        if (realm?.ImageRelease is { } release)
        {
            if (!RealmImageReleases.Authorizes(options, realm)) throw new DeploymentFailure("image_release_conflict");
            if (ContainsDesired(configuration["spec"], existing["spec"]))
            {
                if (ReleaseVersion(existing) != release.Version) throw new DeploymentFailure("image_release_conflict");
            }
            else
            {
                var previous = configuration["spec"]!.DeepClone();
                previous["template"]!["spec"]!["containers"]![0]!["image"] = release.PreviousImage;
                if (!ContainsDesired(previous, existing["spec"]) || ReleaseVersion(existing) != release.PreviousVersion)
                    throw new DeploymentFailure("resource_configuration_drift");
                existing = await ReplaceImageAsync(resource, configuration, existing, release, cancellationToken);
            }
        }
        else if (!ContainsDesired(configuration["spec"], existing["spec"])) throw new DeploymentFailure("resource_configuration_drift");
        if (restartRequired)
            existing = await RestartAsync(resource, desired, existing, realm!.Restart!, activateInference, cancellationToken);
        return existing;
    }

    private static JsonObject? InferenceDisabledConfiguration(JsonObject desired, JsonObject existing)
    {
        var disabled = desired.DeepClone().AsObject();
        var environment = disabled["spec"]!["template"]!["spec"]!["containers"]![0]!["env"]!.AsArray();
        var inference = environment.Where(IsInferenceEnvironment).ToArray();
        if (inference.Length != 2) return null;
        foreach (var variable in inference) environment.Remove(variable);
        // This exception accepts only the exact previous environment, including its order and secret references.
        if (!JsonNode.DeepEquals(environment, existing["spec"]?["template"]?["spec"]?["containers"]?[0]?["env"]) ||
            !ContainsDesired(disabled["spec"], existing["spec"])) return null;
        return disabled;
    }

    private static bool IsInferenceEnvironment(JsonNode? variable) =>
        variable?["name"]?.GetValue<string>() is "ARKYA_INFERENCE_API_URL" or "ARKYA_INFERENCE_SERVICE_KEY";

    private static long RestartRevision(JsonObject deployment)
    {
        var value = deployment["spec"]?["template"]?["metadata"]?["annotations"]?[RestartAnnotation];
        if (value is null) return 0;
        return value is JsonValue scalar && scalar.TryGetValue<string>(out var text) &&
            long.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var revision) && revision > 0
            ? revision : -1;
    }

    private async Task<JsonObject> RestartAsync(string resource, JsonObject desired, JsonObject existing,
        RealmRestartTransition restart, bool activateInference, CancellationToken cancellationToken)
    {
        var resourceVersion = existing["metadata"]?["resourceVersion"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(resourceVersion)) throw new DeploymentFailure("resource_configuration_drift");
        var annotations = existing["spec"]?["template"]?["metadata"]?["annotations"]?.DeepClone().AsObject() ?? new JsonObject();
        annotations[RestartAnnotation] = restart.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var patch = new JsonArray(
            new JsonObject { ["op"] = "test", ["path"] = "/metadata/resourceVersion", ["value"] = resourceVersion },
            new JsonObject { ["op"] = "add", ["path"] = "/spec/template/metadata/annotations", ["value"] = annotations });
        if (activateInference)
            foreach (var variable in desired["spec"]!["template"]!["spec"]!["containers"]![0]!["env"]!.AsArray().Where(IsInferenceEnvironment))
                patch.Add(new JsonObject
                {
                    ["op"] = "add", ["path"] = "/spec/template/spec/containers/0/env/-", ["value"] = variable!.DeepClone()
                });
        using var response = await SendAsync(HttpMethod.Patch, resource, patch, cancellationToken, "application/json-patch+json");
        if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity)
            throw new DeploymentFailure("resource_conflict");
        RequireSuccess(response);
        var updated = await ReadAsync(response, cancellationToken);
        if (!MatchesOwnership(desired, updated)) throw new DeploymentFailure("resource_ownership_conflict");
        if (!ContainsDesired(desired["spec"], updated["spec"]) || RestartRevision(updated) != restart.Revision ||
            ReleaseVersion(updated) != ReleaseVersion(existing))
            throw new DeploymentFailure("resource_configuration_drift");
        updated.Remove("status");
        return updated;
    }

    private static bool MatchesOwnership(JsonObject desired, JsonObject actual) =>
        JsonNode.DeepEquals(desired["metadata"]!["name"], actual["metadata"]?["name"]) &&
        JsonNode.DeepEquals(desired["metadata"]!["namespace"], actual["metadata"]?["namespace"]) &&
        ContainsDesired(desired["metadata"]!["labels"], actual["metadata"]?["labels"]);

    private static long ReleaseVersion(JsonObject deployment)
    {
        var value = deployment["metadata"]?["annotations"]?[ImageReleaseAnnotation];
        if (value is null) return 0;
        return value is JsonValue scalar && scalar.TryGetValue<string>(out var text) &&
            long.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var version) && version > 0
            ? version : -1;
    }

    private async Task<JsonObject> ReplaceImageAsync(string resource, JsonObject desired, JsonObject existing,
        RealmImageTransition release, CancellationToken cancellationToken)
    {
        var resourceVersion = existing["metadata"]?["resourceVersion"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(resourceVersion)) throw new DeploymentFailure("resource_configuration_drift");
        var annotations = existing["metadata"]?["annotations"]?.DeepClone().AsObject() ?? new JsonObject();
        annotations[ImageReleaseAnnotation] = release.Version.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var patch = new JsonArray(
            new JsonObject { ["op"] = "test", ["path"] = "/metadata/resourceVersion", ["value"] = resourceVersion },
            new JsonObject { ["op"] = "test", ["path"] = "/spec/template/spec/containers/0/image", ["value"] = release.PreviousImage },
            new JsonObject { ["op"] = "replace", ["path"] = "/spec/template/spec/containers/0/image", ["value"] = release.Image },
            new JsonObject { ["op"] = "add", ["path"] = "/metadata/annotations", ["value"] = annotations });
        using var response = await SendAsync(HttpMethod.Patch, resource, patch, cancellationToken, "application/json-patch+json");
        if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity)
            throw new DeploymentFailure("resource_conflict");
        RequireSuccess(response);
        var updated = await ReadAsync(response, cancellationToken);
        if (!MatchesOwnership(desired, updated)) throw new DeploymentFailure("resource_ownership_conflict");
        if (!ContainsDesired(desired["spec"], updated["spec"]) || ReleaseVersion(updated) != release.Version)
            throw new DeploymentFailure("resource_configuration_drift");
        // The PATCH response may still carry readiness for the previous generation.
        updated.Remove("status");
        return updated;
    }

    private async Task<JsonObject?> GetAsync(string resource, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, resource, null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        RequireSuccess(response);
        return await ReadAsync(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, JsonNode? body, CancellationToken cancellationToken,
        string mediaType = "application/json")
    {
        // Projected service-account tokens are rotated by Kubernetes.
        var token = (await File.ReadAllTextAsync(options.TokenFile, cancellationToken)).Trim();
        if (token.Length is < 1 or > 32768 || token.Any(char.IsWhiteSpace)) throw new DeploymentFailure("cluster_credentials_invalid");
        using var request = new HttpRequestMessage(method, new Uri(new Uri(options.ApiServer.TrimEnd('/') + "/"), path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, mediaType);
        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private static void RequireSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        throw new DeploymentFailure(response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "cluster_access_denied",
            HttpStatusCode.NotFound => "target_namespace_missing",
            HttpStatusCode.UnprocessableEntity or HttpStatusCode.BadRequest => "cluster_resource_rejected",
            _ => "cluster_unavailable"
        });
    }

    private static async Task<JsonObject> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        // Kubernetes error bodies are never forwarded to operators or persisted in realm records.
        await response.Content.LoadIntoBufferAsync(1024 * 1024, cancellationToken);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken), documentOptions:
            new JsonDocumentOptions { MaxDepth = 64 }) as JsonObject ?? throw new DeploymentFailure("cluster_response_invalid");
    }

    public static string ResourceName(RealmRecord realm, ServerDefinition server)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{realm.DeploymentIdentity}/{server.Id}")))[..16];
        return $"ar-{realm.RealmId[..Math.Min(20, realm.RealmId.Length)]}-{server.Id[..Math.Min(16, server.Id.Length)]}-{hash}";
    }

    private static JsonObject Labels(RealmRecord realm, ServerDefinition server)
    {
        var labels = new JsonObject
        {
            ["app.kubernetes.io/managed-by"] = "arkya-realms",
            ["arkya.online/realm-id"] = realm.RealmId,
            ["arkya.online/server-id"] = server.Id,
            ["arkya.online/deployment-id"] = realm.DeploymentIdentity
        };
        if (realm.Profile.Topology is not null)
        {
            labels["arkya.online/server-kind"] = server.Kind.ToLowerInvariant();
            if (server.IslandId is not null) labels["arkya.online/island-id"] = server.IslandId;
        }
        return labels;
    }

    public static JsonObject DeploymentManifest(RealmRecord realm, ServerDefinition server, RealmRuntimeOptions? runtime = null)
    {
        var container = new JsonObject
        {
            ["name"] = "dedicated", ["image"] = realm.Profile.Image, ["imagePullPolicy"] = "IfNotPresent",
            ["env"] = new JsonArray(
                new JsonObject { ["name"] = "ARKYA_REALM_ID", ["value"] = realm.RealmId },
                new JsonObject { ["name"] = "ARKYA_SERVER_ID", ["value"] = server.Id },
                new JsonObject { ["name"] = "ARKYA_GAME_PORT", ["value"] = server.Port.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                new JsonObject { ["name"] = "ARKYA_GAME_TRANSPORT", ["value"] = server.Transport },
                new JsonObject { ["name"] = "ARKYA_BUILD_ID", ["value"] = server.BuildId }),
            ["ports"] = new JsonArray(new JsonObject { ["name"] = "game", ["containerPort"] = server.Port, ["protocol"] = server.Transport == "WebSocket" ? "TCP" : "UDP" }),
            ["resources"] = new JsonObject
            {
                ["requests"] = new JsonObject { ["cpu"] = "250m", ["memory"] = "256Mi" },
                ["limits"] = new JsonObject { ["cpu"] = "2", ["memory"] = "2Gi" }
            },
            ["securityContext"] = new JsonObject
            {
                ["allowPrivilegeEscalation"] = false,
                ["capabilities"] = new JsonObject { ["drop"] = new JsonArray("ALL") }
            }
        };
        if (realm.Profile.Topology is { } topology)
        {
            var environment = (JsonArray)container["env"]!;
            AddEnvironment("ARKYA_SERVER_KIND", server.Kind);
            AddEnvironment("ARKYA_REALM_TOPOLOGY", JsonSerializer.Serialize(topology, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            if (server.IslandId is not null) AddEnvironment("ARKYA_ISLAND_ID", server.IslandId);
            if (server.PartitionIndex is { } index) AddEnvironment("ARKYA_PARTITION_INDEX", index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (server.PartitionCount is { } count) AddEnvironment("ARKYA_PARTITION_COUNT", count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (server.Bounds is { } bounds)
            {
                AddEnvironment("ARKYA_AREA_BOUNDS", JsonSerializer.Serialize(bounds, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
                var neighbors = realm.Profile.Servers.Where(other => other.Id != server.Id && other.Kind == DedicatedKind.Island &&
                    other.IslandId == server.IslandId && other.Bounds is { } otherBounds && IslandPartitioning.AreNeighbors(bounds, otherBounds))
                    .Select(other => other.Id).ToArray();
                AddEnvironment("ARKYA_AREA_NEIGHBORS", JsonSerializer.Serialize(neighbors));
            }

            void AddEnvironment(string name, string value) => environment.Add(new JsonObject { ["name"] = name, ["value"] = value });
        }
        if (server.Transport == "WebSocket")
        {
            ((JsonArray)container["env"]!).Add(new JsonObject { ["name"] = "ARKYA_READY_FILE", ["value"] = "/tmp/arkya-ready" });
            container["resources"]!["requests"] = new JsonObject { ["cpu"] = "500m", ["memory"] = "1Gi" };
            container["resources"]!["limits"]!["memory"] = "6Gi";
            container["securityContext"]!["readOnlyRootFilesystem"] = true;
            container["volumeMounts"] = new JsonArray(new JsonObject { ["name"] = "tmp", ["mountPath"] = "/tmp" });
            container["readinessProbe"] = new JsonObject
            {
                ["exec"] = new JsonObject { ["command"] = new JsonArray("test", "-f", "/tmp/arkya-ready") },
                ["periodSeconds"] = 2, ["timeoutSeconds"] = 1, ["failureThreshold"] = 2
            };
        }
        if (runtime is { DedicatedKeySeed.Length: > 0 })
        {
            if (!runtime.IsValid()) throw new DeploymentFailure("runtime_configuration_invalid");
            var environment = (JsonArray)container["env"]!;
            environment.Add(new JsonObject { ["name"] = "ARKYA_CHARACTERS_URL", ["value"] = runtime.CharactersUrlFor(realm.RealmId) });
            if (runtime.RealmsBaseUrl.Length > 0)
                environment.Add(new JsonObject { ["name"] = "ARKYA_REALMS_API_URL", ["value"] = runtime.RealmsBaseUrl });
            environment.Add(new JsonObject
            {
                ["name"] = "ARKYA_DEDICATED_KEY",
                ["valueFrom"] = new JsonObject { ["secretKeyRef"] = new JsonObject { ["name"] = DedicatedSecretName, ["key"] = server.Id } }
            });
            if (runtime.AllowClusterHttp)
                environment.Add(new JsonObject { ["name"] = "ARKYA_ALLOW_CLUSTER_HTTP", ["value"] = "1" });
            if (runtime.InferenceBaseUrl.Length > 0)
            {
                environment.Add(new JsonObject { ["name"] = "ARKYA_INFERENCE_API_URL", ["value"] = runtime.InferenceBaseUrl });
                environment.Add(new JsonObject
                {
                    ["name"] = "ARKYA_INFERENCE_SERVICE_KEY",
                    ["valueFrom"] = new JsonObject { ["secretKeyRef"] = new JsonObject { ["name"] = InferenceSecretName, ["key"] = "service-key" } }
                });
            }
        }
        if (server.Args.Length > 0) container["args"] = new JsonArray(server.Args.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());
        if (realm.Profile.EnvironmentSecretName is { } secret)
            container["envFrom"] = new JsonArray(new JsonObject { ["secretRef"] = new JsonObject { ["name"] = secret } });
        var pod = new JsonObject
        {
            ["automountServiceAccountToken"] = false, ["terminationGracePeriodSeconds"] = 30,
            ["securityContext"] = new JsonObject { ["seccompProfile"] = new JsonObject { ["type"] = "RuntimeDefault" } },
            ["containers"] = new JsonArray(container)
        };
        if (server.Transport == "WebSocket")
        {
            pod["securityContext"]!["runAsNonRoot"] = true;
            pod["securityContext"]!["runAsUser"] = 1654;
            pod["securityContext"]!["runAsGroup"] = 1654;
            pod["securityContext"]!["fsGroup"] = 1654;
            pod["nodeSelector"] = new JsonObject { ["kubernetes.io/os"] = "linux", ["kubernetes.io/arch"] = "amd64" };
            pod["volumes"] = new JsonArray(new JsonObject { ["name"] = "tmp", ["emptyDir"] = new JsonObject { ["medium"] = "Memory", ["sizeLimit"] = "512Mi" } });
        }
        if (realm.Profile.ImagePullSecretName is { } pullSecret)
            pod["imagePullSecrets"] = new JsonArray(new JsonObject { ["name"] = pullSecret });
        var metadata = new JsonObject { ["name"] = ResourceName(realm, server), ["namespace"] = realm.Namespace, ["labels"] = Labels(realm, server) };
        if (realm.ImageRelease is { } release)
            metadata["annotations"] = new JsonObject { [ImageReleaseAnnotation] = release.Version.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        var templateMetadata = new JsonObject { ["labels"] = Labels(realm, server) };
        if (realm.Restart is { } restart)
            templateMetadata["annotations"] = new JsonObject { [RestartAnnotation] = restart.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        return new JsonObject
        {
            ["apiVersion"] = "apps/v1", ["kind"] = "Deployment",
            ["metadata"] = metadata,
            ["spec"] = new JsonObject
            {
                ["replicas"] = 1, ["revisionHistoryLimit"] = 1, ["progressDeadlineSeconds"] = server.Transport == "WebSocket" ? 600 : 240,
                ["strategy"] = new JsonObject { ["type"] = "Recreate" },
                ["selector"] = new JsonObject { ["matchLabels"] = Labels(realm, server) },
                ["template"] = new JsonObject { ["metadata"] = templateMetadata, ["spec"] = pod }
            }
        };
    }

    public static JsonObject ServiceManifest(RealmRecord realm, ServerDefinition server) => new()
    {
        ["apiVersion"] = "v1", ["kind"] = "Service",
        ["metadata"] = new JsonObject { ["name"] = ResourceName(realm, server), ["namespace"] = realm.Namespace, ["labels"] = Labels(realm, server) },
        ["spec"] = new JsonObject
        {
            ["type"] = server.Transport == "WebSocket" ? "ClusterIP" : "LoadBalancer", ["selector"] = Labels(realm, server),
            ["ports"] = new JsonArray(new JsonObject { ["name"] = "game", ["port"] = server.Port, ["targetPort"] = server.Port, ["protocol"] = server.Transport == "WebSocket" ? "TCP" : "UDP" })
        }
    };

    private static bool ContainsDesired(JsonNode? desired, JsonNode? actual)
    {
        if (desired is JsonObject expectedObject)
            return actual is JsonObject actualObject && expectedObject.All(pair => actualObject.TryGetPropertyValue(pair.Key, out var value) && ContainsDesired(pair.Value, value));
        if (desired is JsonArray expectedArray)
            return actual is JsonArray actualArray && expectedArray.Count == actualArray.Count &&
                expectedArray.Zip(actualArray).All(pair => ContainsDesired(pair.First, pair.Second));
        return JsonNode.DeepEquals(desired, actual);
    }
}
