using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arkya.Realms.Api.Application;
using Arkya.Realms.Api.Domain;
using Microsoft.Extensions.Options;

namespace Arkya.Realms.Api.Infrastructure;

public sealed class KubernetesRealmDeployer(HttpClient client, IOptions<DeploymentOptions> configured) : IRealmDeployer
{
    private readonly DeploymentOptions options = configured.Value;

    public async Task<ServerObservation[]> ReconcileAsync(RealmRecord realm, CancellationToken cancellationToken)
    {
        if (!options.Enabled || !options.Targets.Any(target => target.Id == realm.TargetId && target.Namespace == realm.Namespace))
            throw new DeploymentFailure("target_unavailable");
        var results = new List<ServerObservation>();
        foreach (var server in realm.Profile.Servers)
        {
            var deployment = await EnsureAsync($"apis/apps/v1/namespaces/{realm.Namespace}/deployments",
                DeploymentManifest(realm, server), cancellationToken);
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
            if (address is { Length: > 253 } || address?.Any(char.IsControl) == true) address = null;
            results.Add(new(server.Id, ready, address));
        }
        return results.ToArray();
    }

    private async Task<JsonObject> EnsureAsync(string collection, JsonObject desired, CancellationToken cancellationToken)
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
        if (!ContainsDesired(desired["metadata"]!["labels"], existing["metadata"]?["labels"]))
            throw new DeploymentFailure("resource_ownership_conflict");
        if (!ContainsDesired(desired["spec"], existing["spec"]))
            throw new DeploymentFailure("resource_configuration_drift");
        return existing;
    }

    private async Task<JsonObject?> GetAsync(string resource, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, resource, null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        RequireSuccess(response);
        return await ReadAsync(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, JsonObject? body, CancellationToken cancellationToken)
    {
        // Projected service-account tokens are rotated by Kubernetes.
        var token = (await File.ReadAllTextAsync(options.TokenFile, cancellationToken)).Trim();
        if (token.Length is < 1 or > 32768 || token.Any(char.IsWhiteSpace)) throw new DeploymentFailure("cluster_credentials_invalid");
        using var request = new HttpRequestMessage(method, new Uri(new Uri(options.ApiServer.TrimEnd('/') + "/"), path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
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

    private static JsonObject Labels(RealmRecord realm, ServerDefinition server) => new()
    {
        ["app.kubernetes.io/managed-by"] = "arkya-realms",
        ["arkya.online/realm-id"] = realm.RealmId,
        ["arkya.online/server-id"] = server.Id,
        ["arkya.online/deployment-id"] = realm.DeploymentIdentity
    };

    public static JsonObject DeploymentManifest(RealmRecord realm, ServerDefinition server)
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
        if (server.Transport == "WebSocket")
        {
            ((JsonArray)container["env"]!).Add(new JsonObject { ["name"] = "ARKYA_READY_FILE", ["value"] = "/tmp/arkya-ready" });
            container["readinessProbe"] = new JsonObject
            {
                ["exec"] = new JsonObject { ["command"] = new JsonArray("test", "-f", "/tmp/arkya-ready") },
                ["periodSeconds"] = 2, ["timeoutSeconds"] = 1, ["failureThreshold"] = 2
            };
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
        if (realm.Profile.ImagePullSecretName is { } pullSecret)
            pod["imagePullSecrets"] = new JsonArray(new JsonObject { ["name"] = pullSecret });
        return new JsonObject
        {
            ["apiVersion"] = "apps/v1", ["kind"] = "Deployment",
            ["metadata"] = new JsonObject { ["name"] = ResourceName(realm, server), ["namespace"] = realm.Namespace, ["labels"] = Labels(realm, server) },
            ["spec"] = new JsonObject
            {
                ["replicas"] = 1, ["revisionHistoryLimit"] = 1, ["progressDeadlineSeconds"] = 240,
                ["strategy"] = new JsonObject { ["type"] = "Recreate" },
                ["selector"] = new JsonObject { ["matchLabels"] = Labels(realm, server) },
                ["template"] = new JsonObject { ["metadata"] = new JsonObject { ["labels"] = Labels(realm, server) }, ["spec"] = pod }
            }
        };
    }

    public static JsonObject ServiceManifest(RealmRecord realm, ServerDefinition server) => new()
    {
        ["apiVersion"] = "v1", ["kind"] = "Service",
        ["metadata"] = new JsonObject { ["name"] = ResourceName(realm, server), ["namespace"] = realm.Namespace, ["labels"] = Labels(realm, server) },
        ["spec"] = new JsonObject
        {
            ["type"] = "LoadBalancer", ["selector"] = Labels(realm, server),
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
