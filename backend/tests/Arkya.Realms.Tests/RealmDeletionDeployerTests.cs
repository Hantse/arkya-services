using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Arkya.Realms.Api.Application;
using Arkya.Realms.Api.Domain;
using Arkya.Realms.Api.Infrastructure;
using Microsoft.Extensions.Options;

namespace Arkya.Realms.Tests;

public sealed class RealmDeletionDeployerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeletesCurrentAndPreviousRuntimeWorkloadsAfterPreflightingBoth(bool previousServiceIsForeign)
    {
        var original = Fixtures.Realm();
        var current = original.Profile.Servers[0] with { Id = "island-next", Port = 17943 };
        var migrated = original with
        {
            Profile = original.Profile with { Servers = [current] },
            RuntimeRelease = new(1, original.Profile, original.ImageRelease, original.Restart) { Completed = true }
        };
        using var cluster = new DeleteCluster(migrated with
        {
            Profile = migrated.Profile with { Servers = [current, original.Profile.Servers[0]] }
        });
        if (previousServiceIsForeign)
        {
            var previousService = cluster.Resources.Single(pair => pair.Key.Contains("/services/", StringComparison.Ordinal) &&
                pair.Value["metadata"]!["labels"]!["arkya.online/server-id"]!.GetValue<string>() == original.Profile.Servers[0].Id).Value;
            previousService["metadata"]!["labels"]!["arkya.online/deployment-id"] = "foreign";
            Assert.Equal("resource_ownership_conflict", (await Assert.ThrowsAsync<DeploymentFailure>(() =>
                cluster.Deployer().DeleteAsync(migrated, default))).Code);
            Assert.Empty(cluster.Deletes);
            Assert.Equal(4, cluster.Resources.Count);
        }
        else
        {
            Assert.True(await cluster.Deployer().DeleteAsync(migrated, default));
            Assert.Equal(4, cluster.Deletes.Length);
            Assert.Empty(cluster.Resources);
        }
    }

    [Fact]
    public async Task DeletesOnlyExactOwnedWorkloadsWithPreconditionsAndForegroundCascade()
    {
        var realm = Fixtures.Realm();
        realm = realm with { Profile = realm.Profile with { Servers = [realm.Profile.Servers[0], realm.Profile.Servers[0] with { Id = "area-east", Port = 17844 }] } };
        using var cluster = new DeleteCluster(realm);
        var preserved = cluster.AddUnrelatedResources();
        Assert.True(await cluster.Deployer().DeleteAsync(realm, default));
        Assert.Equal(4, cluster.Deletes.Length);
        Assert.All(cluster.Deletes, request =>
        {
            Assert.Equal("v1", request.Body!["apiVersion"]!.GetValue<string>());
            Assert.Equal("DeleteOptions", request.Body["kind"]!.GetValue<string>());
            Assert.Equal("Foreground", request.Body["propagationPolicy"]!.GetValue<string>());
            Assert.NotNull(request.Body["preconditions"]!["uid"]);
            Assert.NotNull(request.Body["preconditions"]!["resourceVersion"]);
            Assert.Null(request.Body["gracePeriodSeconds"]);
            Assert.StartsWith(request.Path.StartsWith("apis/", StringComparison.Ordinal)
                ? $"apis/apps/v1/namespaces/{realm.Namespace}/deployments/ar-{realm.RealmId}-"
                : $"api/v1/namespaces/{realm.Namespace}/services/ar-{realm.RealmId}-", request.Path);
        });
        Assert.Equal(preserved, cluster.Resources.ToDictionary(pair => pair.Key, pair => pair.Value.ToJsonString()));
        Assert.True(await cluster.Deployer().DeleteAsync(realm, default));
        Assert.Equal(4, cluster.Deletes.Length);
        Assert.All(cluster.Requests, request => Assert.True(request.Method == HttpMethod.Get || request.Method == HttpMethod.Delete));
    }

    [Fact]
    public async Task WaitsForTerminatingDeploymentsAndServicesWithoutSendingRepeatedDeletes()
    {
        using var cluster = new DeleteCluster { AsynchronousDeletion = true };
        Assert.False(await cluster.Deployer().DeleteAsync(cluster.Realm, default));
        Assert.Equal(2, cluster.Deletes.Length);
        Assert.All(cluster.Resources.Values, resource => Assert.NotNull(resource["metadata"]!["deletionTimestamp"]));
        Assert.False(await cluster.Deployer().DeleteAsync(cluster.Realm, default));
        Assert.Equal(2, cluster.Deletes.Length);
        cluster.Resources.Clear();
        Assert.True(await cluster.Deployer().DeleteAsync(cluster.Realm, default));
        Assert.Equal(2, cluster.Deletes.Length);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("namespace")]
    [InlineData("app.kubernetes.io/managed-by")]
    [InlineData("arkya.online/realm-id")]
    [InlineData("arkya.online/server-id")]
    [InlineData("arkya.online/deployment-id")]
    public async Task RefusesForeignServiceBeforeDeletingItsOwnedDeployment(string changed)
    {
        using var cluster = new DeleteCluster();
        var metadata = cluster.Service["metadata"]!;
        if (changed is "name" or "namespace") metadata[changed] = "foreign";
        else metadata["labels"]![changed] = "foreign";
        var before = cluster.Resources.ToDictionary(pair => pair.Key, pair => pair.Value.ToJsonString());
        Assert.Equal("resource_ownership_conflict", (await Assert.ThrowsAsync<DeploymentFailure>(() =>
            cluster.Deployer().DeleteAsync(cluster.Realm, default))).Code);
        Assert.Empty(cluster.Deletes);
        Assert.Equal(before, cluster.Resources.ToDictionary(pair => pair.Key, pair => pair.Value.ToJsonString()));
    }

    [Theory]
    [InlineData("annotation", "argocd.argoproj.io/tracking-id")]
    [InlineData("annotation", "argocd.argoproj.io/installation-id")]
    [InlineData("annotation", "meta.helm.sh/release-name")]
    [InlineData("label", "app.kubernetes.io/instance")]
    [InlineData("label", "argocd.argoproj.io/instance")]
    [InlineData("label", "kustomize.toolkit.fluxcd.io/name")]
    [InlineData("label", "helm.toolkit.fluxcd.io/name")]
    [InlineData("manager", "argocd-controller")]
    [InlineData("manager", "argocd-application-controller")]
    [InlineData("manager", "kustomize-controller")]
    [InlineData("manager", "helm-controller")]
    [InlineData("owner", "")]
    public async Task RefusesResourcesReconciledByAnotherController(string source, string marker)
    {
        using var cluster = new DeleteCluster();
        var metadata = cluster.Service["metadata"]!;
        if (source == "annotation") metadata["annotations"] = new JsonObject { [marker] = "external-controller" };
        else if (source == "label") metadata["labels"]![marker] = "external-controller";
        else if (source == "manager") metadata["managedFields"] = new JsonArray(new JsonObject { ["manager"] = marker });
        else metadata["ownerReferences"] = new JsonArray(new JsonObject { ["uid"] = "external-controller" });
        Assert.Equal("resource_protected", (await Assert.ThrowsAsync<DeploymentFailure>(() =>
            cluster.Deployer().DeleteAsync(cluster.Realm, default))).Code);
        Assert.Empty(cluster.Deletes);
    }

    [Theory]
    [InlineData("uid")]
    [InlineData("resourceVersion")]
    public async Task RefusesIncompleteClusterIdentityWithoutAnyMutation(string field)
    {
        using var cluster = new DeleteCluster();
        cluster.Service["metadata"]!.AsObject().Remove(field);
        Assert.Equal("cluster_response_invalid", (await Assert.ThrowsAsync<DeploymentFailure>(() =>
            cluster.Deployer().DeleteAsync(cluster.Realm, default))).Code);
        Assert.Empty(cluster.Deletes);
    }

    [Theory]
    [InlineData("target")]
    [InlineData("namespace")]
    [InlineData("realm")]
    [InlineData("disabled")]
    public async Task RequiresAnEnabledExactApprovedTargetBeforeReadingTheCluster(string mismatch)
    {
        using var cluster = new DeleteCluster();
        var options = cluster.Configuration();
        if (mismatch == "target") options.Targets[0].Id = "another-target";
        if (mismatch == "namespace") options.Targets[0].Namespace = "another-namespace";
        if (mismatch == "realm") options.Targets[0].RealmId = "another-realm";
        if (mismatch == "disabled") options.Enabled = false;
        Assert.Equal("target_unavailable", (await Assert.ThrowsAsync<DeploymentFailure>(() =>
            cluster.Deployer(options).DeleteAsync(cluster.Realm, default))).Code);
        Assert.Empty(cluster.Requests);
    }

    [Fact]
    public async Task ConcurrentClusterMutationFailsTheVersionPreconditionAndRevalidatesOnRetry()
    {
        using var cluster = new DeleteCluster { ChangeBeforeDelete = true };
        Assert.Equal("resource_conflict", (await Assert.ThrowsAsync<DeploymentFailure>(() =>
            cluster.Deployer().DeleteAsync(cluster.Realm, default))).Code);
        Assert.Equal(2, cluster.Resources.Count);
        cluster.ChangeBeforeDelete = false;
        Assert.True(await cluster.Deployer().DeleteAsync(cluster.Realm, default));
        Assert.Equal("changed", cluster.Deletes[1].Body!["preconditions"]!["resourceVersion"]!.GetValue<string>());
    }

    [Fact]
    public async Task MissingDeleteResponsesResumeIdempotentlyWithoutRecreation()
    {
        using var cluster = new DeleteCluster { LoseDeleteResponse = true };
        await Assert.ThrowsAsync<HttpRequestException>(() => cluster.Deployer().DeleteAsync(cluster.Realm, default));
        Assert.Single(cluster.Resources);
        cluster.LoseDeleteResponse = false;
        Assert.True(await cluster.Deployer().DeleteAsync(cluster.Realm, default));
        Assert.Equal(2, cluster.Deletes.Length);
        Assert.Empty(cluster.Resources);
    }

    [Fact]
    public async Task ConcurrentDeletionReturningNotFoundCountsAsAbsent()
    {
        using var cluster = new DeleteCluster { DeleteReturnsNotFound = true };
        Assert.True(await cluster.Deployer().DeleteAsync(cluster.Realm, default));
        Assert.Empty(cluster.Resources);
    }

    [Fact]
    public async Task ClusterDenialIsSanitizedAndDoesNotRemoveTheWorkload()
    {
        using var cluster = new DeleteCluster { DeleteDenied = true };
        var error = await Assert.ThrowsAsync<DeploymentFailure>(() => cluster.Deployer().DeleteAsync(cluster.Realm, default));
        Assert.Equal("cluster_access_denied", error.Code);
        Assert.DoesNotContain("private-cluster-response", error.ToString());
        Assert.Equal(2, cluster.Resources.Count);
    }

    [Fact]
    public async Task CleansOwnedFixedCredentialsOnlyAfterAllDedicatedWorkloadsHaveDisappeared()
    {
        using var cluster = new DeleteCluster { AsynchronousDeletion = true };
        cluster.AddOwnedSecret("arkya-dedicated");
        cluster.AddOwnedSecret("arkya-ghcr");
        Assert.False(await cluster.Deployer().DeleteAsync(cluster.Realm, default));
        Assert.DoesNotContain(cluster.Requests, request => request.Path.Contains("/secrets/", StringComparison.Ordinal));
        Assert.Equal(2, cluster.Deletes.Length);
        cluster.CompleteTerminatingResources();

        Assert.False(await cluster.Deployer().DeleteAsync(cluster.Realm, default));
        Assert.Equal(4, cluster.Deletes.Length);
        Assert.All(cluster.Deletes.Skip(2), request =>
        {
            Assert.Contains("/secrets/arkya-", request.Path);
            Assert.NotNull(request.Body!["preconditions"]!["uid"]);
            Assert.NotNull(request.Body["preconditions"]!["resourceVersion"]);
            Assert.DoesNotContain("private-credential-data", request.Body.ToJsonString());
        });
        Assert.False(await cluster.Deployer().DeleteAsync(cluster.Realm, default));
        Assert.Equal(4, cluster.Deletes.Length);
        cluster.CompleteTerminatingResources();
        Assert.True(await cluster.Deployer().DeleteAsync(cluster.Realm, default));
        Assert.True(await cluster.Deployer().DeleteAsync(cluster.Realm, default));
        Assert.Equal(4, cluster.Deletes.Length);
        Assert.Empty(cluster.Resources);
    }

    [Theory]
    [InlineData("arkya-dedicated", "app.kubernetes.io/managed-by")]
    [InlineData("arkya-dedicated", "arkya.online/realm-id")]
    [InlineData("arkya-dedicated", "arkya.online/deployment-id")]
    [InlineData("arkya-ghcr", "app.kubernetes.io/managed-by")]
    [InlineData("arkya-ghcr", "arkya.online/realm-id")]
    [InlineData("arkya-ghcr", "arkya.online/deployment-id")]
    public async Task PreservesForeignOrSharedFixedCredentials(string name, string changedLabel)
    {
        using var cluster = new DeleteCluster();
        var secret = cluster.AddOwnedSecret(name);
        secret["metadata"]!["labels"]![changedLabel] = "shared-or-foreign";
        secret["metadata"]!["annotations"] = new JsonObject { ["argocd.argoproj.io/tracking-id"] = "shared-gitops" };
        var before = secret.ToJsonString();
        Assert.True(await cluster.Deployer().DeleteAsync(cluster.Realm, default));
        Assert.Equal(2, cluster.Deletes.Length);
        Assert.Equal(before, Assert.Single(cluster.Resources).Value.ToJsonString());
    }

    [Fact]
    public async Task ProtectsTrackedOwnedCredentialsBeforeDeletingAnySecret()
    {
        using var cluster = new DeleteCluster();
        var dedicated = cluster.AddOwnedSecret("arkya-dedicated");
        var registry = cluster.AddOwnedSecret("arkya-ghcr");
        registry["metadata"]!["annotations"] = new JsonObject { ["argocd.argoproj.io/tracking-id"] = "gitops-owned" };
        var dedicatedBefore = dedicated.ToJsonString();
        Assert.Equal("resource_protected", (await Assert.ThrowsAsync<DeploymentFailure>(() =>
            cluster.Deployer().DeleteAsync(cluster.Realm, default))).Code);
        Assert.Equal(2, cluster.Deletes.Length);
        Assert.DoesNotContain(cluster.Deletes, request => request.Path.Contains("/secrets/", StringComparison.Ordinal));
        Assert.Equal(dedicatedBefore, dedicated.ToJsonString());
        Assert.Equal(2, cluster.Resources.Count);
    }

    [Fact]
    public async Task ConcurrentSecretMutationUsesTheSameVersionFenceAndRetriesSafely()
    {
        using var cluster = new DeleteCluster { ChangeSecretBeforeDelete = true };
        cluster.AddOwnedSecret("arkya-dedicated");
        Assert.Equal("resource_conflict", (await Assert.ThrowsAsync<DeploymentFailure>(() =>
            cluster.Deployer().DeleteAsync(cluster.Realm, default))).Code);
        Assert.Single(cluster.Resources);
        cluster.ChangeSecretBeforeDelete = false;
        Assert.True(await cluster.Deployer().DeleteAsync(cluster.Realm, default));
        Assert.Equal("changed", cluster.Deletes.Last().Body!["preconditions"]!["resourceVersion"]!.GetValue<string>());
        Assert.Empty(cluster.Resources);
    }

    private sealed record Request(HttpMethod Method, string Path, JsonObject? Body);

    private sealed class DeleteCluster : HttpMessageHandler
    {
        private readonly string tokenFile = Path.GetTempFileName();
        private readonly HttpClient client;
        public RealmRecord Realm { get; }
        public Dictionary<string, JsonObject> Resources { get; } = [];
        public List<Request> Requests { get; } = [];
        public Request[] Deletes => Requests.Where(request => request.Method == HttpMethod.Delete).ToArray();
        public JsonObject Service => Resources.Values.First(resource => resource["kind"]!.GetValue<string>() == "Service");
        public bool AsynchronousDeletion { get; set; }
        public bool ChangeBeforeDelete { get; set; }
        public bool ChangeSecretBeforeDelete { get; set; }
        public bool LoseDeleteResponse { get; set; }
        public bool DeleteReturnsNotFound { get; set; }
        public bool DeleteDenied { get; set; }

        public DeleteCluster(RealmRecord? realm = null)
        {
            Realm = realm ?? Fixtures.Realm();
            File.WriteAllText(tokenFile, "synthetic-cluster-token");
            client = new HttpClient(this, false);
            foreach (var server in Realm.Profile.Servers)
            {
                Add($"apis/apps/v1/namespaces/{Realm.Namespace}/deployments", KubernetesRealmDeployer.DeploymentManifest(Realm, server));
                Add($"api/v1/namespaces/{Realm.Namespace}/services", KubernetesRealmDeployer.ServiceManifest(Realm, server));
            }
        }

        private void Add(string collection, JsonObject resource)
        {
            var name = resource["metadata"]!["name"]!.GetValue<string>();
            resource["metadata"]!["uid"] = $"uid-{Resources.Count}";
            resource["metadata"]!["resourceVersion"] = $"version-{Resources.Count}";
            Resources.Add($"{collection}/{name}", resource);
        }

        public JsonObject AddOwnedSecret(string name)
        {
            var secret = new JsonObject
            {
                ["apiVersion"] = "v1", ["kind"] = "Secret", ["type"] = "Opaque",
                ["metadata"] = new JsonObject
                {
                    ["name"] = name, ["namespace"] = Realm.Namespace,
                    ["labels"] = new JsonObject
                    {
                        ["app.kubernetes.io/managed-by"] = "arkya-realms", ["arkya.online/realm-id"] = Realm.RealmId,
                        ["arkya.online/deployment-id"] = Realm.DeploymentIdentity
                    }
                },
                ["data"] = new JsonObject { ["key"] = "private-credential-data" }
            };
            Add($"api/v1/namespaces/{Realm.Namespace}/secrets", secret);
            return secret;
        }

        public void CompleteTerminatingResources()
        {
            foreach (var path in Resources.Where(pair => pair.Value["metadata"]?["deletionTimestamp"] is not null).Select(pair => pair.Key).ToArray())
                Resources.Remove(path);
        }

        public Dictionary<string, string> AddUnrelatedResources()
        {
            var result = new Dictionary<string, string>();
            foreach (var resource in new[] { ("secrets", "arkya-dedicated"), ("secrets", "arkya-ghcr"), ("services", "arkya-beta-web-island"), ("namespaces", Realm.Namespace) })
            {
                var path = $"api/v1/namespaces/{Realm.Namespace}/{resource.Item1}/{resource.Item2}";
                var value = new JsonObject { ["unrelated"] = resource.Item2 };
                Resources.Add(path, value);
                result.Add(path, value.ToJsonString());
            }
            return result;
        }

        public DeploymentOptions Configuration()
        {
            var options = Fixtures.Options();
            options.TokenFile = tokenFile;
            options.ApiServer = "https://cluster.example";
            return options;
        }

        public KubernetesRealmDeployer Deployer(DeploymentOptions? options = null) => new(client, Options.Create(options ?? Configuration()));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath.TrimStart('/');
            var body = request.Content is null ? null : JsonNode.Parse(await request.Content.ReadAsStringAsync(cancellationToken))!.AsObject();
            Requests.Add(new(request.Method, path, body));
            if (!Resources.TryGetValue(path, out var existing)) return Response(HttpStatusCode.NotFound);
            if (request.Method == HttpMethod.Get) return Response(HttpStatusCode.OK, existing.ToJsonString());
            Assert.Equal(HttpMethod.Delete, request.Method);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            if (DeleteDenied) return Response(HttpStatusCode.Forbidden, "private-cluster-response");
            if (ChangeBeforeDelete || ChangeSecretBeforeDelete && path.Contains("/secrets/", StringComparison.Ordinal))
                existing["metadata"]!["resourceVersion"] = "changed";
            var conditions = body!["preconditions"]!;
            if (!JsonNode.DeepEquals(existing["metadata"]!["uid"], conditions["uid"]) ||
                !JsonNode.DeepEquals(existing["metadata"]!["resourceVersion"], conditions["resourceVersion"]))
                return Response(HttpStatusCode.Conflict);
            if (AsynchronousDeletion) existing["metadata"]!["deletionTimestamp"] = "2026-10-23T12:00:00Z";
            else Resources.Remove(path);
            if (LoseDeleteResponse) throw new HttpRequestException("Lost delete response");
            return Response(DeleteReturnsNotFound ? HttpStatusCode.NotFound : HttpStatusCode.Accepted);
        }

        private static HttpResponseMessage Response(HttpStatusCode status, string body = "{}") => new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                client.Dispose();
                File.Delete(tokenFile);
            }
            base.Dispose(disposing);
        }
    }
}
