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
    public string? GameVersion { get; set; }
    public string? SourceRevision { get; set; }
    public string? SourceRunId { get; set; }

    public bool IsValid() => DeploymentOptions.ValidId(RealmId) && DeploymentOptions.ValidId(ProfileId) && Version > 0 &&
        PreviousProfileSha256.Length == 64 && PreviousProfileSha256.All(value => value is >= '0' and <= '9' or >= 'a' and <= 'f') &&
        Topology is not null && RealmTopologyBuilder.IsValid(Topology) &&
        RealmBuildMetadata.Valid(GameVersion, SourceRevision, SourceRunId);
}

public static class RealmRuntimeReleases
{
    public static string Fingerprint(ProfileSnapshot profile) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(profile)));

    public static RealmRuntimeRelease? Find(DeploymentOptions options, string realmId) =>
        options.RuntimeReleases.SingleOrDefault(value => value.RealmId == realmId);

    public static ProfileSnapshot? ApprovedProfile(DeploymentOptions options, RealmRecord realm, RealmRuntimeRelease release)
    {
        if (!release.IsValid() || release.RealmId != realm.RealmId ||
            !options.Targets.Any(target => target.Id == realm.TargetId && target.Namespace == realm.Namespace &&
                (target.RealmId is null || target.RealmId == realm.RealmId))) return null;
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

    public static (RealmRuntimeRelease Release, ProfileSnapshot Profile)? ManualApproval(DeploymentOptions options, RealmRecord realm)
    {
        var release = Find(options, realm.RealmId);
        if (release is null || release.Automatic || realm.Revision == long.MaxValue || realm.Version == long.MaxValue) return null;
        if (realm.RuntimeRelease is { Completed: false })
            return realm.Status == RealmStatus.Failed && Authorizes(options, realm) ? (release, realm.Profile) : null;
        if (realm.Status != RealmStatus.Ready || release.Version <= (realm.RuntimeRelease?.Version ?? 0) ||
            Fingerprint(realm.Profile) != release.PreviousProfileSha256 ||
            ApprovedProfile(options, realm, release) is not { } profile || !CompatibleTransition(realm.Profile, profile)) return null;
        return (release, profile);
    }

    internal static RealmRecord BeginTransition(RealmRecord realm, RealmRuntimeRelease release, ProfileSnapshot profile,
        DateTimeOffset now, string actor, string reason)
    {
        var transition = realm.RuntimeRelease is { Completed: false } retry ? retry :
            new RealmRuntimeTransition(release.Version, realm.Profile, realm.ImageRelease, realm.Restart);
        var revision = realm.Revision + 1;
        return realm with
        {
            Profile = profile, RuntimeRelease = transition, ImageRelease = null, Restart = null,
            Revision = revision, Version = realm.Version + 1, RequestedFromRevision = null,
            Status = RealmStatus.Pending, LastError = null, UpdatedAt = now, DeploymentStartedAt = now,
            Servers = profile.Servers.Select(value => new ServerObservation(value.Id, 0, null)).ToArray(),
            Operations = realm.Operations.Append(new RealmOperation("RuntimeMigration", revision, realm.Revision,
                actor, reason, now, null, RealmStatus.Pending, null, transition.PreviousProfile.Image, profile.Image)).TakeLast(100).ToArray()
        };
    }

    public static async Task<RealmRecord?> PrepareAsync(RealmRecord realm, IRealmRepository repository,
        DeploymentOptions options, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (realm.RuntimeRelease is { Completed: false }) return Authorizes(options, realm) ? realm : null;
        var release = Find(options, realm.RealmId);
        if (release is null || !release.Automatic || release.Version <= (realm.RuntimeRelease?.Version ?? 0)) return realm;
        if (realm.Status != RealmStatus.Ready || realm.Revision == long.MaxValue || realm.Version == long.MaxValue ||
            Fingerprint(realm.Profile) != release.PreviousProfileSha256 ||
            ApprovedProfile(options, realm, release) is not { } profile || !CompatibleTransition(realm.Profile, profile)) return null;
        var now = clock.GetUtcNow();
        var next = BeginTransition(realm, release, profile, now, "gitops", $"Approved runtime release {release.Version}");
        return await repository.TryReplaceAsync(next, realm.Version, cancellationToken) ? next : null;
    }
}

internal static class RealmBuildMetadata
{
    public static bool Valid(string? version, string? revision, string? runId) =>
        (string.IsNullOrEmpty(version) || version.Length <= 64 && version.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '+' or '_')) &&
        (string.IsNullOrEmpty(revision) || revision.Length is 40 or 64 && revision.All(char.IsAsciiHexDigit)) &&
        (string.IsNullOrEmpty(runId) || runId.Length <= 24 && runId.All(char.IsAsciiDigit));
}
