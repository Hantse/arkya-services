using Arkya.Realms.Api.Domain;

namespace Arkya.Realms.Api.Application;

internal static class RealmImageReleases
{
    public static RealmImageRelease? Find(DeploymentOptions options, string realmId) =>
        options.ImageReleases.SingleOrDefault(release => release.RealmId == realmId);

    public static DeploymentProfile? AllowedProfile(DeploymentOptions options, RealmRecord realm, RealmImageRelease release)
    {
        if (!release.IsValid() || release.RealmId != realm.RealmId ||
            !options.Targets.Any(target => target.Id == realm.TargetId && target.Namespace == realm.Namespace &&
                (target.RealmId is null || target.RealmId == realm.RealmId))) return null;
        var profile = options.Profiles.SingleOrDefault(value => value.Id == release.ProfileId);
        if (profile is null || !profile.IsValid() || realm.Profile.Topology is not null && !RealmTopologyBuilder.IsValid(realm.Profile.Topology)) return null;
        return SameConfiguration(RealmTopologyBuilder.Snapshot(profile, realm.RealmId, realm.Profile.Topology), realm.Profile) ? profile : null;
    }

    public static bool Authorizes(DeploymentOptions options, RealmRecord realm)
    {
        var transition = realm.ImageRelease;
        var release = Find(options, realm.RealmId);
        if (transition is null || release is null || transition.PreviousVersion >= transition.Version ||
            transition.PreviousVersion < 0 || transition.Image != realm.Profile.Image ||
            AllowedProfile(options, realm, release) is not { } profile) return false;
        if (release.Version == transition.Version)
            return release.PreviousImage == transition.PreviousImage && profile.Image == transition.Image;
        // An unrequested manual release explicitly retains its predecessor as the running image.
        return !release.Automatic && release.Version > transition.Version && release.PreviousImage == transition.Image;
    }

    // Releases may replace only the image; topology, credentials and protocol remain frozen.
    private static bool SameConfiguration(ProfileSnapshot left, ProfileSnapshot right) =>
        left.Id == right.Id && left.EnvironmentSecretName == right.EnvironmentSecretName &&
        left.ImagePullSecretName == right.ImagePullSecretName && left.Servers.Length == right.Servers.Length &&
        left.TopologyRuntimeSupported == right.TopologyRuntimeSupported && SameTopology(left.Topology, right.Topology) &&
        left.Servers.Zip(right.Servers).All(pair => pair.First.Id == pair.Second.Id && pair.First.Port == pair.Second.Port &&
            pair.First.Transport == pair.Second.Transport && pair.First.PublicEndpoint == pair.Second.PublicEndpoint &&
            pair.First.ProtocolVersion == pair.Second.ProtocolVersion && pair.First.BuildId == pair.Second.BuildId &&
            pair.First.Kind == pair.Second.Kind && pair.First.IslandId == pair.Second.IslandId &&
            pair.First.Bounds == pair.Second.Bounds &&
            pair.First.PartitionIndex == pair.Second.PartitionIndex && pair.First.PartitionCount == pair.Second.PartitionCount &&
            pair.First.Args.SequenceEqual(pair.Second.Args, StringComparer.Ordinal));

    private static bool SameTopology(RealmTopology? left, RealmTopology? right) =>
        left is null ? right is null : right is not null && left.InstanceDedicatedCount == right.InstanceDedicatedCount &&
        left.RaidDedicatedCount == right.RaidDedicatedCount && left.Islands.SequenceEqual(right.Islands);
}
