namespace Arkya.Realms.Api.Domain;

public static class RealmStatus
{
    public const string Draft = "Draft";
    public const string Pending = "Pending";
    public const string Deploying = "Deploying";
    public const string Ready = "Ready";
    public const string Failed = "Failed";
}

public sealed record ServerDefinition(string Id, int Port, string[] Args);
public sealed record ProfileSnapshot(string Id, string Image, ServerDefinition[] Servers,
    string? EnvironmentSecretName, string? ImagePullSecretName);
public sealed record ServerObservation(string Id, int ReadyReplicas, string? Address);

public sealed record RealmRecord(string RealmId, string DisplayName, string TargetId, string Namespace,
    ProfileSnapshot Profile, string DeploymentIdentity, long Revision, long Version, long? RequestedFromRevision,
    string Status, string? LastError, DateTimeOffset UpdatedAt, DateTimeOffset? DeploymentStartedAt,
    ServerObservation[] Servers);
