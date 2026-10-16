using Arkya.Realms.Api.Domain;

namespace Arkya.Realms.Api.Contracts;

public sealed record CreateRealmRequest(string RealmId, string DisplayName, string TargetId, string ProfileId);
public sealed record DeployRealmRequest(long ExpectedRevision);
public sealed record RealmServerSummary(string Id, int Port, int ReadyReplicas, int DesiredReplicas, string? Address);
public sealed record RealmSummary(string RealmId, string DisplayName, string TargetId, string Namespace,
    string ProfileId, string Image, long Revision, string Status, string? LastError, DateTimeOffset UpdatedAt,
    RealmServerSummary[] Servers)
{
    public static RealmSummary From(RealmRecord realm) => new(realm.RealmId, realm.DisplayName, realm.TargetId,
        realm.Namespace, realm.Profile.Id, realm.Profile.Image, realm.Revision, realm.Status, realm.LastError,
        realm.UpdatedAt, realm.Profile.Servers.Select(server =>
        {
            var observation = realm.Servers.FirstOrDefault(value => value.Id == server.Id);
            return new RealmServerSummary(server.Id, server.Port, observation?.ReadyReplicas ?? 0,
                realm.Status == RealmStatus.Draft ? 0 : 1, observation?.Address);
        }).ToArray());
}
public sealed record TargetSummary(string Id, string DisplayName, string Namespace);
public sealed record ProfileServerSummary(string Id, int Port);
public sealed record ProfileSummary(string Id, string DisplayName, string Image, ProfileServerSummary[] Servers);
public sealed record DeploymentOptionsResponse(TargetSummary[] Targets, ProfileSummary[] Profiles, bool DeploymentEnabled);
public sealed record ErrorResponse(string Code, string Message);
