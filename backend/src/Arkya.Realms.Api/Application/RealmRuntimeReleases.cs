using System.Security.Cryptography;
using System.Text.Json;
using Arkya.Realms.Api.Domain;

namespace Arkya.Realms.Api.Application;

public sealed class RealmRuntimeRelease
{
    public string RealmId { get; set; } = "";
    public string ProfileId { get; set; } = "";
    public long Version { get; set; }
    public string PreviousProfileSha256 { get; set; } = "";
    public RealmTopology? Topology { get; set; }
    public bool Automatic { get; set; }

    public bool IsValid() => DeploymentOptions.ValidId(RealmId) && DeploymentOptions.ValidId(ProfileId) && Version > 0 &&
        PreviousProfileSha256.Length == 64 && PreviousProfileSha256.All(value => value is >= '0' and <= '9' or >= 'a' and <= 'f') &&
        Topology is not null && RealmTopologyBuilder.IsValid(Topology);
}

public static class RealmRuntimeReleases
{
    public static string Fingerprint(ProfileSnapshot profile) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(profile)));

    public static RealmRuntimeRelease? Find(DeploymentOptions options, string realmId) =>
        options.RuntimeReleases.SingleOrDefault(value => value.RealmId == realmId);

    public static ProfileSnapshot? ApprovedProfile(DeploymentOptions options, RealmRecord realm, RealmRuntimeRelease release)
    {
        if (!release.IsValid() || !options.Targets.Any(target => target.Id == realm.TargetId &&
                target.Namespace == realm.Namespace && target.RealmId == realm.RealmId)) return null;
        var profile = options.Profiles.SingleOrDefault(value => value.Id == release.ProfileId);
        if (profile is null || !profile.IsValid()) return null;
        ProfileSnapshot snapshot;
        try { snapshot = RealmTopologyBuilder.Snapshot(profile, realm.RealmId, release.Topology); }
        catch (ArgumentException) { return null; }
        return RealmTopologyBuilder.SnapshotSupportsRuntime(snapshot) ? snapshot : null;
    }

    public static bool Authorizes(DeploymentOptions options, RealmRecord realm)
    {
        var transition = realm.RuntimeRelease;
        var release = Find(options, realm.RealmId);
        if (transition is null || release is null || transition.Version != release.Version ||
            Fingerprint(transition.PreviousProfile) != release.PreviousProfileSha256 ||
            ApprovedProfile(options, realm, release) is not { } profile || Fingerprint(profile) != Fingerprint(realm.Profile)) return false;
        return CompatibleTransition(transition.PreviousProfile, profile);
    }

    public static bool CompatibleTransition(ProfileSnapshot previous, ProfileSnapshot next) =>
        previous.Image != next.Image && previous.EnvironmentSecretName == next.EnvironmentSecretName &&
        previous.ImagePullSecretName == next.ImagePullSecretName &&
        !previous.Servers.Select(value => value.Id).Intersect(next.Servers.Select(value => value.Id), StringComparer.Ordinal).Any();

    public static async Task<RealmRecord?> PrepareAsync(RealmRecord realm, IRealmRepository repository,
        DeploymentOptions options, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (realm.RuntimeRelease is { Completed: false }) return Authorizes(options, realm) ? realm : null;
        var release = Find(options, realm.RealmId);
        if (release is null || !release.Automatic || release.Version <= (realm.RuntimeRelease?.Version ?? 0)) return realm;
        if (realm.Status != RealmStatus.Ready || realm.Revision == long.MaxValue ||
            Fingerprint(realm.Profile) != release.PreviousProfileSha256 ||
            ApprovedProfile(options, realm, release) is not { } profile || !CompatibleTransition(realm.Profile, profile)) return null;
        var now = clock.GetUtcNow();
        var revision = realm.Revision + 1;
        var next = realm with
        {
            Profile = profile,
            RuntimeRelease = new(release.Version, realm.Profile, realm.ImageRelease, realm.Restart),
            ImageRelease = null, Restart = null, Revision = revision, Version = realm.Version + 1,
            RequestedFromRevision = null, Status = RealmStatus.Pending, LastError = null,
            UpdatedAt = now, DeploymentStartedAt = now,
            Servers = profile.Servers.Select(value => new ServerObservation(value.Id, 0, null)).ToArray(),
            Operations = realm.Operations.Append(new RealmOperation("RuntimeMigration", revision, realm.Revision,
                "gitops", $"Approved runtime release {release.Version}", now, null, RealmStatus.Pending, null,
                realm.Profile.Image, profile.Image)).TakeLast(100).ToArray()
        };
        return await repository.TryReplaceAsync(next, realm.Version, cancellationToken) ? next : null;
    }
}
