using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Arkya.ServiceDefaults;

namespace Arkya.Admin.Realms;

public sealed class RealmsApiOptions
{
    public bool Enabled { get; init; }
    public string BaseUrl { get; init; } = "https://localhost:7144";
    public string AdminKey { get; init; } = "";
    public bool AllowClusterHttp { get; init; }
    public string CertificateAuthorityFile { get; init; } = "";
}

public sealed record RealmServer(string Id, int Port, int ReadyReplicas, int DesiredReplicas, string? Address,
    string Transport = "Udp", string? PublicEndpoint = null)
{
    public string Kind { get; init; } = "World";
    public string? IslandId { get; init; }
    public int? PartitionIndex { get; init; }
    public int? PartitionCount { get; init; }
    public AreaBounds? Bounds { get; init; }
}
public sealed record RealmSummary(string RealmId, string DisplayName, string TargetId, string Namespace,
    string ProfileId, string Image, long Revision, string Status, string? LastError, DateTimeOffset UpdatedAt, RealmServer[] Servers,
    RealmOperation[]? Operations = null)
{
    public RealmTopology? Topology { get; init; }
    public bool TopologyRuntimeSupported { get; init; }
    public string ProfileSha256 { get; init; } = "";
}
public sealed record RealmOperation(string Kind, long Revision, string RequestedBy, string Reason,
    DateTimeOffset RequestedAt, DateTimeOffset? CompletedAt, string Status, string? Error, string PreviousImage, string Image);
public sealed record RealmOperationsOptions(bool CanRestart, bool CanUpdate, string? UpdateImage, long? UpdateVersion, string? UnavailableReason,
    string? UpdateKind = null, int? CurrentProtocolVersion = null, string? CurrentBuildId = null,
    int? TargetProtocolVersion = null, string? TargetBuildId = null, string? TargetGameVersion = null,
    string? TargetSourceRevision = null, string? TargetSourceRunId = null);
public sealed record RealmCommandServer(string ServerId, DateTimeOffset? LastSeenAt, bool Available, string[] SupportedCommands,
    int ConfigurationVersion = 0, long? AppliedConfigurationRevision = null);
public sealed record RealmServerVersion(string ServerId, DateTimeOffset? LastSeenAt, bool Available,
    string? GameVersion, string? SourceRevision, string? SourceRunId);
public sealed record RealmConfiguration(long Revision, decimal XpMultiplier, decimal HarvestMultiplier,
    DateTimeOffset? UpdatedAt, string? UpdatedBy, RealmConfigurationChange[] History);
public sealed record RealmConfigurationChange(long Revision, long ExpectedRevision, decimal XpMultiplier,
    decimal HarvestMultiplier, decimal PreviousXpMultiplier, decimal PreviousHarvestMultiplier,
    DateTimeOffset UpdatedAt, string UpdatedBy, string Reason);
public sealed record UpdateRealmConfiguration(long ExpectedRevision, decimal XpMultiplier, decimal HarvestMultiplier, string Reason);
public sealed record RealmCommand(string Id, string ServerId, string Type, string? Message, string? CharacterId,
    string Status, string Actor, string Reason, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt,
    DateTimeOffset? CompletedAt, string? ResultCode, int? AffectedPlayers);
public sealed record RealmCommands(RealmCommandServer[] Servers, RealmCommand[] Commands);
public sealed record RealmCommandRequest(string RequestId, long ExpectedRevision, string ServerId, string Type,
    string Message, string CharacterId, string Reason);
public sealed record RealmTarget(string Id, string DisplayName, string Namespace, string? RealmId = null);
public sealed record RealmProfileServer(string Id, int Port, string Transport = "Udp", string? PublicEndpoint = null);
public sealed record RealmProfile(string Id, string DisplayName, string Image, RealmProfileServer[] Servers)
{
    public bool TopologySupported { get; init; }
}
public sealed record RealmDeploymentOptions(RealmTarget[] Targets, RealmProfile[] Profiles, bool DeploymentEnabled);
public sealed record AreaBounds(double MinX, double MinZ, double MaxX, double MaxZ);
public sealed record RealmIslandTopology(string IslandId, int DedicatedCount, double Width = 1000, double Depth = 1000,
    double OriginX = 0, double OriginZ = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? GridColumns = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? GridRows = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ContentId { get; init; }
}
public sealed record RealmTopology(RealmIslandTopology[] Islands, int InstanceDedicatedCount, int RaidDedicatedCount)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public GameAreaTransition[]? AreaTransitions { get; init; }
}
public sealed record CreateRealmRequest(string RealmId, string DisplayName, string TargetId, string ProfileId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RealmTopology? Topology = null);

public sealed class RealmsApiException(HttpStatusCode statusCode, string message) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}

public sealed class RealmsAdminClient(HttpClient client, IOptions<RealmsApiOptions> options)
{
    public bool Enabled => options.Value.Enabled;
    public Task<RealmSummary[]> ListAsync(CancellationToken ct) => SendAsync<RealmSummary[]>(HttpMethod.Get, "admin/realms", null, ct);
    public Task<RealmDeploymentOptions> OptionsAsync(CancellationToken ct) => SendAsync<RealmDeploymentOptions>(HttpMethod.Get, "admin/realms/options", null, ct);
    public Task<RealmSummary> GetAsync(string realmId, CancellationToken ct) => SendAsync<RealmSummary>(HttpMethod.Get,
        "admin/realms/" + Uri.EscapeDataString(realmId), null, ct);
    public Task<RealmSummary> CreateAsync(CreateRealmRequest realm, CancellationToken ct) => SendAsync<RealmSummary>(HttpMethod.Post, "admin/realms", realm, ct);
    public Task<RealmSummary> UpdateTopologyAsync(string realmId, long expectedRevision, string profileId, RealmTopology? topology, CancellationToken ct) =>
        SendAsync<RealmSummary>(HttpMethod.Put, "admin/realms/" + Uri.EscapeDataString(realmId) + "/topology",
            new { expectedRevision, profileId, topology }, ct);
    public Task<RealmSummary> DeployAsync(string realmId, long expectedRevision, CancellationToken ct) => SendAsync<RealmSummary>(HttpMethod.Post,
        "admin/realms/" + Uri.EscapeDataString(realmId) + "/deploy", new { expectedRevision }, ct);
    public Task<RealmOperationsOptions> OperationsOptionsAsync(string realmId, CancellationToken ct) => SendAsync<RealmOperationsOptions>(HttpMethod.Get,
        "admin/realms/" + Uri.EscapeDataString(realmId) + "/operations-options", null, ct);
    public Task<RealmSummary> RestartAsync(string realmId, long expectedRevision, string reason, string actor, CancellationToken ct) =>
        SendAsync<RealmSummary>(HttpMethod.Post, "admin/realms/" + Uri.EscapeDataString(realmId) + "/restart",
            new { expectedRevision, reason }, ct, actor);
    public Task<RealmSummary> UpdateAsync(string realmId, long expectedRevision, string reason, string expectedImage, string actor, CancellationToken ct) =>
        SendAsync<RealmSummary>(HttpMethod.Post, "admin/realms/" + Uri.EscapeDataString(realmId) + "/update",
            new { expectedRevision, reason, expectedImage }, ct, actor);
    public Task<RealmSummary> DeleteAsync(string realmId, long expectedRevision, string reason, string confirmRealmId, string actor, CancellationToken ct) =>
        SendAsync<RealmSummary>(HttpMethod.Post, "admin/realms/" + Uri.EscapeDataString(realmId) + "/delete",
            new { expectedRevision, reason, confirmRealmId }, ct, actor);
    public Task<RealmCommands> CommandsAsync(string realmId, CancellationToken ct) => SendAsync<RealmCommands>(HttpMethod.Get,
        "admin/realms/" + Uri.EscapeDataString(realmId) + "/commands", null, ct);
    public Task<RealmServerVersion[]> ServerVersionsAsync(string realmId, CancellationToken ct) => SendAsync<RealmServerVersion[]>(HttpMethod.Get,
        "admin/realms/" + Uri.EscapeDataString(realmId) + "/server-versions", null, ct);
    public Task<RealmCommand> CommandAsync(string realmId, RealmCommandRequest command, string actor, CancellationToken ct) =>
        SendAsync<RealmCommand>(HttpMethod.Post, "admin/realms/" + Uri.EscapeDataString(realmId) + "/commands", command, ct, actor);
    public Task<RealmConfiguration> ConfigurationAsync(string realmId, CancellationToken ct) => SendAsync<RealmConfiguration>(HttpMethod.Get,
        "admin/realms/" + Uri.EscapeDataString(realmId) + "/configuration", null, ct);
    public Task<RealmConfiguration> UpdateConfigurationAsync(string realmId, UpdateRealmConfiguration configuration, string actor, CancellationToken ct) =>
        SendAsync<RealmConfiguration>(HttpMethod.Put, "admin/realms/" + Uri.EscapeDataString(realmId) + "/configuration", configuration, ct, actor);

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct, string? actor = null)
    {
        if (!Enabled) throw new RealmsApiException(HttpStatusCode.ServiceUnavailable, "Realm management is disabled on this installation.");
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Arkya-Admin-Key", options.Value.AdminKey);
        if (actor is not null) request.Headers.Add("X-Arkya-Admin-Actor", actor);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var message = response.StatusCode switch
            {
                HttpStatusCode.Conflict => "This realm already exists, has changed or has a deployment in progress. Refresh before trying again.",
                HttpStatusCode.BadRequest => "Check the fields, reason and availability of this action.",
                HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized => "Access to realms was denied. Check the administration configuration.",
                HttpStatusCode.NotFound => "This realm does not exist or is no longer available.",
                _ => "The realm service is unavailable. Try again shortly."
            };
            throw new RealmsApiException(response.StatusCode, message);
        }
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(ct)
                ?? throw new RealmsApiException(HttpStatusCode.BadGateway, "The realm service returned an empty response.");
        }
        catch (JsonException)
        {
            throw new RealmsApiException(HttpStatusCode.BadGateway, "The realm service returned an invalid response.");
        }
    }
}

public static class RealmsHttpTransport
{
    public static SocketsHttpHandler Create(string certificateAuthorityFile)
    {
        var handler = new SocketsHttpHandler { AllowAutoRedirect = false };
        if (!string.IsNullOrEmpty(certificateAuthorityFile))
        {
            var policy = new X509ChainPolicy
            {
                TrustMode = X509ChainTrustMode.CustomRootTrust,
                // The private cluster CA does not publish an OCSP or CRL endpoint.
                RevocationMode = X509RevocationMode.NoCheck
            };
            policy.CustomTrustStore.Add(X509Certificate2.CreateFromPem(File.ReadAllText(certificateAuthorityFile)));
            handler.SslOptions.CertificateChainPolicy = policy;
        }
        // The platform still validates the server name and the full certificate chain.
        return handler;
    }
}

public static class RealmPresentation
{
    private const string UnityRepositoryUrl = "https://github.com/Hantse/arkya-mmorpg";
    public static string? SourceCommitUrl(string? revision) => revision is { Length: 40 or 64 } && revision.All(char.IsAsciiHexDigit)
        ? UnityRepositoryUrl + "/commit/" + revision : null;
    public static string? SourceRunUrl(string? runId) => runId is { Length: > 0 and <= 24 } && runId.All(char.IsAsciiDigit) && runId.Any(character => character != '0')
        ? UnityRepositoryUrl + "/actions/runs/" + runId : null;
    public static string OperationLabel(string kind) => kind switch
    {
        "Update" => "Update", "RuntimeMigration" => "Runtime migration", "Delete" => "Deletion", _ => "Restart"
    };
    public static string UpdateUnavailableMessage(string? reason) => reason switch
    {
        "deployment_disabled" => "Deployment is disabled on this installation. Enable deployment before installing an approved release.",
        "target_unavailable" => "The deployment target is no longer configured. Restore this target in the Realms service.",
        "topology_runtime_unsupported" => "The current profile does not support this realm's runtime. Check the approved profile before updating.",
        "operation_in_progress" => "Updates are unavailable in the current state. Refresh after the current operation finishes.",
        "runtime_migration_unavailable" => "The approved runtime migration does not match the current realm configuration. Refresh or check the GitOps approval.",
        "no_approved_update" => "No approved image update or runtime migration matches this realm. A successful build must also be approved in GitOps before installation.",
        _ => "Update is unavailable. Refresh and check this realm's state and approved release configuration."
    };
    public static string CommandLabel(string type) => type switch
    {
        "announce" => "In-game announcement", "kick" => "Disconnect a character", "save" => "Save players", _ => "Command"
    };
    public static string CommandStatusLabel(string status) => status switch
    {
        "Pending" => "Awaiting server", "Dispatched" => "Sent to server", "Succeeded" => "Executed",
        "Failed" => "Failed", "Expired" => "Expired without sending", "Unknown" => "Unknown result", _ => "Unknown state"
    };
    public static string CommandStatusClass(string status) => status switch
    {
        "Succeeded" => "ready", "Failed" or "Expired" or "Unknown" => "failed", _ => "pending"
    };
    public static string CommandFailureMessage(string? resultCode) => resultCode switch
    {
        "player_not_found" => "This character is no longer connected to this server.",
        "unsupported" => "The server does not support this command.",
        _ => "The server could not execute the command. Check its status before trying again."
    };
    public static string FailureMessage(string? code) => code switch
    {
        "target_unavailable" => "The deployment target is no longer configured. Restore this target in the Realms service.",
        "deployment_progress_failed" => "The cluster reports a startup failure. Check the dedicated server events and logs.",
        "resource_conflict" => "A resource changed during creation. Check the realm resources before trying again.",
        "resource_deleting" => "A realm resource is being deleted. Wait for this operation to finish before trying again.",
        "resource_ownership_conflict" => "An existing resource belongs to another owner. Check names and labels in the target namespace.",
        "resource_configuration_drift" => "Cluster resources differ from the saved configuration. Restore the expected configuration before trying again.",
        "cluster_credentials_invalid" => "The cluster credentials are invalid. Check the service account token.",
        "registry_credentials_unreadable" => "The GHCR credentials cannot be read. Check the registry Secret mount in Realms.",
        "registry_credentials_invalid" => "The GHCR credentials are invalid. Check the registry Secret's Docker file.",
        "runtime_configuration_invalid" => "The dedicated profile is incompatible with the runtime or registry configuration.",
        "cluster_access_denied" => "The cluster denied access. Check the service account and its permissions in the target namespace.",
        "target_namespace_missing" => "The target namespace does not exist. Create it and configure its permissions before trying again.",
        "cluster_resource_rejected" => "The cluster rejected the requested resources. Check the profile and target namespace constraints.",
        "cluster_unavailable" => "The cluster is not responding. Check its availability and the Realms service connection.",
        "cluster_response_invalid" => "The cluster returned an invalid response. Check its availability before trying again.",
        "deployment_timeout" => "The servers did not become ready within the deadline. Check their image, resources and cluster events.",
        _ => "Deployment failed. Check cluster events and the Realms service configuration before trying again."
    };
    public static bool CanDeploy(RealmSummary realm) => realm.Status is "Draft" or "Failed";
    public static bool CanDelete(RealmSummary realm) => realm.Status is "Draft" or "Ready" or "Failed" or "DeleteFailed"
        && !(realm.Operations?.Any(operation => operation.Status is "Pending" or "Deploying" or "Deleting") ?? false);
    public static string DeleteFailureMessage(string? code) => code switch
    {
        "deletion_timeout" => "The realm resources were not removed within the deadline. Check the cluster before retrying deletion.",
        "resource_protected" => "A realm resource is protected. Check its protection in the target namespace before retrying deletion.",
        "resource_ownership_conflict" => "A resource has a different owner. Check realm labels in the target namespace before retrying deletion.",
        "cluster_access_denied" => "The cluster denied deletion. Check service account permissions in the target namespace.",
        _ => "Deletion failed. Check the realm resources and cluster availability before trying again."
    };
    public static string ServerKindLabel(string kind) => kind switch
    {
        "Island" => "Area partition", "Instance" => "Instance", "Raid" => "Raid", _ => "Open world"
    };
    public static string StatusLabel(string status) => status switch
    {
        "Draft" => "Draft",
        "Pending" => "Pending",
        "Deploying" => "Deploying",
        "Ready" => "Deployment ready",
        "Failed" => "Deployment failed",
        "Deleting" => "Deleting",
        "DeleteFailed" => "Deletion failed",
        "Deleted" => "Deleted",
        _ => "Unknown state"
    };
    public static string StatusClass(string status) => status switch
    {
        "Ready" => "ready", "Failed" or "DeleteFailed" => "failed", "Pending" or "Deploying" or "Deleting" => "pending", _ => "draft"
    };
    public static bool IsDependencyError(Exception ex, CancellationToken ct) => ex is RealmsApiException or HttpRequestException
        || ex is OperationCanceledException && !ct.IsCancellationRequested;
    public static string ErrorMessage(Exception ex) => ex is RealmsApiException ? ex.Message
        : "The realm service is unavailable. Try again shortly.";
}
