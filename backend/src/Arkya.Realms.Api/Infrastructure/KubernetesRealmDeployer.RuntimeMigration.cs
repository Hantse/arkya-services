using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Arkya.Realms.Api.Application;
using Arkya.Realms.Api.Domain;
using Arkya.ServiceDefaults;

namespace Arkya.Realms.Api.Infrastructure;

public sealed partial class KubernetesRealmDeployer
{
    private async Task<bool> RetirePreviousRuntimeAsync(RealmRecord realm, CancellationToken cancellationToken)
    {
        if (!RealmRuntimeReleases.Authorizes(ReleaseOptions(realm), realm)) throw new DeploymentFailure("runtime_release_conflict");
        var transition = realm.RuntimeRelease!;
        var previous = realm with
        {
            Profile = transition.PreviousProfile, RuntimeRelease = null,
            ImageRelease = transition.PreviousImageRelease, Restart = transition.PreviousRestart
        };
        var stopped = true;
        foreach (var server in previous.Profile.Servers)
        {
            var desired = DeploymentManifest(previous, server, runtimeOptions);
            var resource = $"apis/apps/v1/namespaces/{realm.Namespace}/deployments/{ResourceName(previous, server)}";
            var existing = await GetAsync(resource, cancellationToken);
            if (existing is null)
            {
                stopped &= await PreviousPodsAreGoneAsync(previous, server, cancellationToken);
                continue;
            }
            if (!MatchesOwnership(desired, existing)) throw new DeploymentFailure("resource_ownership_conflict");
            var replicas = existing["spec"]?["replicas"]?.GetValue<int>() ?? 1;
            if (replicas is not (0 or 1)) throw new DeploymentFailure("resource_configuration_drift");
            var expectedSpec = desired["spec"]!.DeepClone();
            expectedSpec["replicas"] = replicas;
            if (!ContainsDesired(expectedSpec, existing["spec"]) ||
                ReleaseVersion(existing) != (previous.ImageRelease?.Version ?? 0) ||
                RestartRevision(existing) != (previous.Restart?.Revision ?? 0))
                throw new DeploymentFailure("resource_configuration_drift");
            if (replicas == 1)
            {
                var version = existing["metadata"]?["resourceVersion"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(version)) throw new DeploymentFailure("resource_configuration_drift");
                var patch = new JsonArray(
                    new JsonObject { ["op"] = "test", ["path"] = "/metadata/resourceVersion", ["value"] = version },
                    new JsonObject { ["op"] = "test", ["path"] = "/spec/replicas", ["value"] = 1 },
                    new JsonObject { ["op"] = "replace", ["path"] = "/spec/replicas", ["value"] = 0 });
                using var response = await SendAsync(HttpMethod.Patch, resource, patch, cancellationToken, "application/json-patch+json");
                if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity)
                    throw new DeploymentFailure("resource_conflict");
                RequireSuccess(response);
                stopped = false;
                continue;
            }
            var generation = existing["metadata"]?["generation"]?.GetValue<long>() ?? 1;
            var status = existing["status"];
            var reportedStopped = status is not null && (status["observedGeneration"]?.GetValue<long>() ?? 0) >= generation &&
                new[] { "replicas", "readyReplicas", "availableReplicas", "updatedReplicas", "terminatingReplicas" }
                    .All(field => (status[field]?.GetValue<int>() ?? 0) == 0);
            stopped &= reportedStopped && await PreviousPodsAreGoneAsync(previous, server, cancellationToken);
        }
        return stopped;
    }

    private async Task<bool> PreviousPodsAreGoneAsync(RealmRecord realm, ServerDefinition server, CancellationToken cancellationToken)
    {
        var labels = Labels(realm, server);
        var selector = string.Join(',', labels.Select(label => $"{label.Key}={label.Value!.GetValue<string>()}"));
        var collection = $"api/v1/namespaces/{realm.Namespace}/pods?limit=100&labelSelector={Uri.EscapeDataString(selector)}";
        var continuation = "";
        var found = false;
        // Deployment counters may omit terminating Pods when their feature gate is disabled.
        for (var page = 0; page < 32; page++)
        {
            var response = await GetAsync(collection + (continuation.Length == 0 ? "" : "&continue=" + Uri.EscapeDataString(continuation)), cancellationToken);
            if (response?["items"] is not JsonArray pods || pods.Count > 100 || response["metadata"] is not JsonObject metadata)
                throw new DeploymentFailure("cluster_response_invalid");
            foreach (var pod in pods)
            {
                if (pod?["metadata"] is not JsonObject owner || owner["namespace"] is not JsonValue namespaceValue ||
                    !namespaceValue.TryGetValue<string>(out var podNamespace) || podNamespace != realm.Namespace ||
                    owner["name"] is not JsonValue name || !name.TryGetValue<string>(out var podName) || string.IsNullOrWhiteSpace(podName) ||
                    !ContainsDesired(labels, owner["labels"]))
                    throw new DeploymentFailure("resource_ownership_conflict");
                found = true;
            }
            if (metadata["continue"] is null) continuation = "";
            else if (metadata["continue"] is JsonValue token && token.TryGetValue<string>(out var value) && value.Length <= 4096 && !value.Any(char.IsControl))
                continuation = value;
            else throw new DeploymentFailure("cluster_response_invalid");
            if (continuation.Length == 0) return !found;
        }
        throw new DeploymentFailure("cluster_response_invalid");
    }

    private async Task<JsonObject> MigrateDedicatedSecretAsync(RealmRecord realm, string resource,
        JsonObject desired, JsonObject existing, CancellationToken cancellationToken)
    {
        if (realm.RuntimeRelease is not { Completed: false } transition || !RealmRuntimeReleases.Authorizes(ReleaseOptions(realm), realm) ||
            !JsonNode.DeepEquals(desired["type"], existing["type"]))
            throw new DeploymentFailure("resource_configuration_drift");
        var previousData = new JsonObject();
        foreach (var server in transition.PreviousProfile.Servers)
            previousData[server.Id] = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                RealmRuntimeKeys.DeriveDedicatedKey(runtimeOptions.DedicatedKeySeed, realm.RealmId, server.Id)));
        if (!JsonNode.DeepEquals(previousData, existing["data"])) throw new DeploymentFailure("resource_configuration_drift");
        var version = existing["metadata"]?["resourceVersion"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(version)) throw new DeploymentFailure("resource_configuration_drift");
        var patch = new JsonArray(
            new JsonObject { ["op"] = "test", ["path"] = "/metadata/resourceVersion", ["value"] = version },
            new JsonObject { ["op"] = "replace", ["path"] = "/data", ["value"] = desired["data"]!.DeepClone() });
        using var response = await SendAsync(HttpMethod.Patch, resource, patch, cancellationToken, "application/json-patch+json");
        if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity)
            throw new DeploymentFailure("resource_conflict");
        RequireSuccess(response);
        var updated = await ReadAsync(response, cancellationToken);
        if (!ContainsDesired(desired["metadata"]!["labels"], updated["metadata"]?["labels"]))
            throw new DeploymentFailure("resource_ownership_conflict");
        return updated;
    }
}
