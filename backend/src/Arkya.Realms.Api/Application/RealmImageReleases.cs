using Arkya.Realms.Api.Domain;

namespace Arkya.Realms.Api.Application;

internal static class RealmImageReleases
{
    public static RealmImageRelease? Find(DeploymentOptions options, string realmId) =>
        options.ImageReleases.SingleOrDefault(release => release.RealmId == realmId);

    public static DeploymentProfile? AllowedProfile(DeploymentOptions options, RealmRecord realm, RealmImageRelease release)
    {
        if (!release.IsValid() || !options.Targets.Any(target => target.Id == realm.TargetId &&
            target.Namespace == realm.Namespace && target.RealmId == realm.RealmId)) return null;
        var profile = options.Profiles.SingleOrDefault(value => value.Id == release.ProfileId);
        return profile is not null && profile.IsValid() && SameConfiguration(profile.Snapshot(), realm.Profile) ? profile : null;
    }

    public static bool Authorizes(DeploymentOptions options, RealmRecord realm)
    {
        var transition = realm.ImageRelease;
        var release = Find(options, realm.RealmId);
        return transition is not null && release is not null && release.Version == transition.Version &&
            release.PreviousImage == transition.PreviousImage && transition.PreviousVersion < transition.Version &&
            transition.PreviousVersion >= 0 && transition.Image == realm.Profile.Image &&
            AllowedProfile(options, realm, release)?.Image == transition.Image;
    }

    // Releases may replace only the image; topology, credentials and protocol remain frozen.
    private static bool SameConfiguration(ProfileSnapshot left, ProfileSnapshot right) =>
        left.Id == right.Id && left.EnvironmentSecretName == right.EnvironmentSecretName &&
        left.ImagePullSecretName == right.ImagePullSecretName && left.Servers.Length == right.Servers.Length &&
        left.Servers.Zip(right.Servers).All(pair => pair.First.Id == pair.Second.Id && pair.First.Port == pair.Second.Port &&
            pair.First.Transport == pair.Second.Transport && pair.First.PublicEndpoint == pair.Second.PublicEndpoint &&
            pair.First.ProtocolVersion == pair.Second.ProtocolVersion && pair.First.BuildId == pair.Second.BuildId &&
            pair.First.Args.SequenceEqual(pair.Second.Args, StringComparer.Ordinal));
}
