using Arkya.Realms.Api.Domain;

namespace Arkya.Realms.Api.Application;

public sealed class RealmNativeReleaseOptions
{
    public bool Enabled { get; set; }
    public string Namespace { get; set; } = "arkya-beta";
    public string ConfigMapName { get; set; } = "arkya-native-runtime-release";
    public int PollIntervalSeconds { get; set; } = 15;
    public RealmNativeReleaseTarget[] Targets { get; set; } = [];
    public NativePublishedRelease? InitialRelease { get; set; }

    public bool IsValid() => DeploymentOptions.ValidNamespace(Namespace) &&
        DeploymentOptions.ValidNamespace(ConfigMapName) && PollIntervalSeconds is >= 2 and <= 300 &&
        Targets is { Length: <= 100 } && Targets.All(target => target is not null &&
            DeploymentOptions.ValidId(target.RealmId) && DeploymentOptions.ValidId(target.ProfileId)) &&
        Targets.Select(target => target.RealmId).Distinct(StringComparer.Ordinal).Count() == Targets.Length &&
        (InitialRelease is null || InitialRelease.IsValid()) && (!Enabled || Targets.Length > 0);
}

public sealed class RealmNativeReleaseTarget
{
    public string RealmId { get; set; } = "";
    public string ProfileId { get; set; } = "";
}
