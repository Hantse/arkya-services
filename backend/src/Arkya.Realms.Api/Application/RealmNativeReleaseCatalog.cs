using Arkya.Realms.Api.Domain;
using Microsoft.Extensions.Options;

namespace Arkya.Realms.Api.Application;

public sealed record RealmReleaseSelection(DeploymentOptions Options, RealmNativeReleasePin? Approval);

public interface IRealmNativeReleaseCatalog
{
    RealmReleaseSelection Select(DeploymentOptions options, RealmRecord realm, bool propose = true);
}

public sealed class RealmNativeReleaseCatalog(IOptions<RealmNativeReleaseOptions> configured) : IRealmNativeReleaseCatalog
{
    private readonly RealmNativeReleaseOptions settings = configured.Value;
    private readonly object gate = new();
    private NativePublishedRelease? latest = configured.Value.InitialRelease is { } initial && initial.IsValid() ? initial : null;

    public NativePublishedRelease? Current => Volatile.Read(ref latest);

    public bool Accept(NativePublishedRelease release)
    {
        if (!release.IsValid()) return false;
        lock (gate)
        {
            if (latest is not null && (release.Version < latest.Version || release.Version == latest.Version && release != latest)) return false;
            Volatile.Write(ref latest, release);
            return true;
        }
    }

    public RealmReleaseSelection Select(DeploymentOptions options, RealmRecord realm, bool propose = true)
    {
        var pin = realm.NativeRelease;
        var target = settings.Enabled ? settings.Targets.SingleOrDefault(value => value.RealmId == realm.RealmId) : null;
        var template = target is null ? null : options.Profiles.SingleOrDefault(value => value.Id == target.ProfileId);
        var allowed = options.Enabled && options.Targets.Any(value => value.Id == realm.TargetId && value.Namespace == realm.Namespace &&
            (value.RealmId is null || value.RealmId == realm.RealmId));
        var retained = pin is null ? new RealmReleaseSelection(options, null) : Restore(options, realm, pin, template, allowed);
        if (pin is not null && retained.Approval is null) return retained;
        var release = Current;
        if (!propose || !allowed || template is null || realm.Status != RealmStatus.Ready ||
            realm.RuntimeRelease is { Completed: false } || release is null || release.Version <= (pin?.Release.Version ?? 0) ||
            release.Image == realm.Profile.Image || !MatchesPair(template, release) || realm.Profile.Topology is null) return retained;
        var profile = WithImage(template, release.Image);
        ProfileSnapshot snapshot;
        try { snapshot = RealmTopologyBuilder.Snapshot(profile, realm.RealmId, realm.Profile.Topology); }
        catch (ArgumentException) { return retained; }
        if (!RealmTopologyBuilder.SnapshotSupportsRuntime(snapshot)) return retained;
        var kind = realm.Profile.Servers.All(server => server.ProtocolVersion == release.ProtocolVersion && server.BuildId == release.BuildId)
            ? "Image" : "RuntimeMigration";
        if (kind == "Image")
        {
            if (release.Version <= (realm.ImageRelease?.Version ?? 0) ||
                RealmRuntimeReleases.Fingerprint(snapshot with { Image = realm.Profile.Image }) != RealmRuntimeReleases.Fingerprint(realm.Profile)) return retained;
        }
        else if (release.Version <= (realm.RuntimeRelease?.Version ?? 0) ||
            !RealmRuntimeReleases.CompatibleTransition(realm.Profile, snapshot)) return retained;
        var approval = new RealmNativeReleasePin(release, kind, profile.Id, RealmRuntimeReleases.Fingerprint(realm.Profile),
            realm.Profile.Image, RealmRuntimeReleases.Fingerprint(snapshot));
        return new(Overlay(options, realm, approval, profile), approval);
    }

    private static RealmReleaseSelection Restore(DeploymentOptions options, RealmRecord realm, RealmNativeReleasePin pin,
        DeploymentProfile? template, bool allowed)
    {
        if (!allowed || template is null || !pin.IsValid() || template.Id != pin.ProfileId || realm.Profile.Id != pin.ProfileId ||
            realm.Profile.Image != pin.Release.Image || !MatchesPair(template, pin.Release) || realm.Profile.Topology is null)
            return Blocked(options, realm.RealmId);
        var profile = WithImage(template, pin.Release.Image);
        ProfileSnapshot snapshot;
        try { snapshot = RealmTopologyBuilder.Snapshot(profile, realm.RealmId, realm.Profile.Topology); }
        catch (ArgumentException) { return Blocked(options, realm.RealmId); }
        if (RealmRuntimeReleases.Fingerprint(snapshot) != pin.TargetProfileSha256 ||
            RealmRuntimeReleases.Fingerprint(realm.Profile) != pin.TargetProfileSha256) return Blocked(options, realm.RealmId);
        if (pin.Kind == "RuntimeMigration")
        {
            if (realm.RuntimeRelease is not { } transition || transition.Version != pin.Release.Version ||
                transition.PreviousProfile.Image != pin.PreviousImage ||
                RealmRuntimeReleases.Fingerprint(transition.PreviousProfile) != pin.PreviousProfileSha256 ||
                !RealmRuntimeReleases.CompatibleTransition(transition.PreviousProfile, snapshot)) return Blocked(options, realm.RealmId);
        }
        else if (realm.ImageRelease is not { } transition || transition.Version != pin.Release.Version ||
            transition.PreviousImage != pin.PreviousImage || transition.Image != pin.Release.Image ||
            RealmRuntimeReleases.Fingerprint(realm.Profile with { Image = pin.PreviousImage }) != pin.PreviousProfileSha256)
            return Blocked(options, realm.RealmId);
        return new(Overlay(options, realm, pin, profile), pin);
    }

    private static bool MatchesPair(DeploymentProfile profile, NativePublishedRelease release) => profile.IsValid() &&
        profile.Servers.All(server => server.ProtocolVersion == release.ProtocolVersion && server.BuildId == release.BuildId);

    private static RealmReleaseSelection Blocked(DeploymentOptions options, string realmId)
    {
        var copy = Copy(options);
        copy.Enabled = false;
        copy.ImageReleases = copy.ImageReleases.Where(value => value.RealmId != realmId).ToArray();
        copy.RuntimeReleases = copy.RuntimeReleases.Where(value => value.RealmId != realmId).ToArray();
        return new(copy, null);
    }

    private static DeploymentOptions Overlay(DeploymentOptions options, RealmRecord realm, RealmNativeReleasePin approval, DeploymentProfile profile)
    {
        var copy = Copy(options);
        copy.Profiles = copy.Profiles.Select(value => value.Id == profile.Id ? profile : value).ToArray();
        copy.ImageReleases = copy.ImageReleases.Where(value => value.RealmId != realm.RealmId).ToArray();
        copy.RuntimeReleases = copy.RuntimeReleases.Where(value => value.RealmId != realm.RealmId).ToArray();
        if (approval.Kind == "Image") copy.ImageReleases = [.. copy.ImageReleases, new()
        {
            RealmId = realm.RealmId, ProfileId = profile.Id, Version = approval.Release.Version,
            PreviousImage = approval.PreviousImage, Automatic = false
        }];
        else copy.RuntimeReleases = [.. copy.RuntimeReleases, new()
        {
            RealmId = realm.RealmId, ProfileId = profile.Id, Version = approval.Release.Version,
            PreviousProfileSha256 = approval.PreviousProfileSha256, Topology = realm.Profile.Topology, Automatic = false,
            GameVersion = approval.Release.GameVersion, SourceRevision = approval.Release.SourceRevision, SourceRunId = approval.Release.SourceRunId
        }];
        return copy;
    }

    private static DeploymentOptions Copy(DeploymentOptions options) => new()
    {
        Enabled = options.Enabled, ApiServer = options.ApiServer, TokenFile = options.TokenFile,
        CertificateAuthorityFile = options.CertificateAuthorityFile, ReconcileIntervalSeconds = options.ReconcileIntervalSeconds,
        DeploymentTimeoutSeconds = options.DeploymentTimeoutSeconds,
        Targets = options.Targets.Select(value => new DeploymentTarget
            { Id = value.Id, DisplayName = value.DisplayName, Namespace = value.Namespace, RealmId = value.RealmId }).ToArray(),
        Profiles = options.Profiles.Select(value => WithImage(value, value.Image)).ToArray(),
        ImageReleases = options.ImageReleases.ToArray(), RuntimeReleases = options.RuntimeReleases.ToArray()
    };

    private static DeploymentProfile WithImage(DeploymentProfile profile, string image) => new()
    {
        Id = profile.Id, DisplayName = profile.DisplayName, Image = image, EnvironmentSecretName = profile.EnvironmentSecretName,
        ImagePullSecretName = profile.ImagePullSecretName, TopologyTemplateServerId = profile.TopologyTemplateServerId,
        TopologyServerIdPrefix = profile.TopologyServerIdPrefix, TopologyPublicEndpointTemplate = profile.TopologyPublicEndpointTemplate,
        Servers = profile.Servers.Select(server => new DeploymentServer
        {
            Id = server.Id, Port = server.Port, Args = server.Args.ToArray(), Transport = server.Transport,
            PublicEndpoint = server.PublicEndpoint, ProtocolVersion = server.ProtocolVersion, BuildId = server.BuildId
        }).ToArray()
    };
}
