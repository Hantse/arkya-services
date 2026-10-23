using System.Text.RegularExpressions;
using Arkya.Realms.Api.Domain;

namespace Arkya.Realms.Api.Application;

public sealed class DeploymentOptions
{
    public bool Enabled { get; set; }
    public string ApiServer { get; set; } = "https://kubernetes.default.svc";
    public string TokenFile { get; set; } = "/var/run/secrets/kubernetes.io/serviceaccount/token";
    public string CertificateAuthorityFile { get; set; } = "/var/run/secrets/kubernetes.io/serviceaccount/ca.crt";
    public int ReconcileIntervalSeconds { get; set; } = 10;
    public int DeploymentTimeoutSeconds { get; set; } = 300;
    public DeploymentTarget[] Targets { get; set; } = [];
    public DeploymentProfile[] Profiles { get; set; } = [];
    public RealmImageRelease[] ImageReleases { get; set; } = [];
    public RealmRuntimeRelease[] RuntimeReleases { get; set; } = [];

    public bool IsValid() => Targets is not null && Profiles is not null && ImageReleases is not null && RuntimeReleases is not null &&
        RuntimeReleases.Length <= 100 && RuntimeReleases.All(release => release is not null && release.IsValid() &&
            Targets.Count(target => target is not null && target.RealmId == release.RealmId) == 1 &&
            Profiles.Any(profile => profile is not null && profile.Id == release.ProfileId)) &&
        RuntimeReleases.Select(release => release.RealmId).Distinct().Count() == RuntimeReleases.Length &&
        Targets.Length <= 100 && Profiles.Length <= 50 && ImageReleases.Length <= 100 &&
        ReconcileIntervalSeconds is >= 2 and <= 300 && DeploymentTimeoutSeconds is >= 30 and <= 3600 &&
        Uri.TryCreate(ApiServer, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
        string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) && uri.AbsolutePath == "/" &&
        !string.IsNullOrWhiteSpace(TokenFile) && !string.IsNullOrWhiteSpace(CertificateAuthorityFile) &&
        Targets.All(target => target is not null && ValidId(target.Id) && ValidNamespace(target.Namespace) && ValidName(target.DisplayName) &&
            (target.RealmId is null || ValidId(target.RealmId))) &&
        Targets.Select(target => target.Id).Distinct().Count() == Targets.Length &&
        Targets.Select(target => target.Namespace).Distinct().Count() == Targets.Length &&
        Profiles.All(profile => profile is not null && profile.IsValid()) &&
        Profiles.Select(profile => profile.Id).Distinct().Count() == Profiles.Length &&
        ImageReleases.All(release => release is not null && release.IsValid() &&
            Targets.Count(target => target.RealmId == release.RealmId) == 1 &&
            Profiles.Any(profile => profile.Id == release.ProfileId && profile.Image != release.PreviousImage)) &&
        ImageReleases.Select(release => release.RealmId).Distinct().Count() == ImageReleases.Length &&
        (!Enabled || Targets.Length > 0 && Profiles.Length > 0);

    public static bool ValidId(string? value) => value is not null && Regex.IsMatch(value, @"\A[a-z][a-z0-9-]{1,29}[a-z0-9]\z", RegexOptions.CultureInvariant);
    public static bool ValidNamespace(string? value) => value is not null && value.Length <= 63 && Regex.IsMatch(value, @"\A[a-z0-9]([a-z0-9-]*[a-z0-9])?\z", RegexOptions.CultureInvariant) &&
        value != "default" && !value.StartsWith("kube-", StringComparison.Ordinal);
    public static bool ValidName(string? value) => value is { Length: >= 1 and <= 80 } && value == value.Trim() && !value.Any(char.IsControl);
    public static bool ValidSecret(string? value) => string.IsNullOrEmpty(value) || value.Length <= 253 && Regex.IsMatch(value, @"\A[a-z0-9]([a-z0-9.-]*[a-z0-9])?\z", RegexOptions.CultureInvariant);
    public static bool ValidImage(string? value) => value is { Length: <= 512 } &&
        Regex.IsMatch(value, @"\A[a-z0-9][a-z0-9._:/-]*@sha256:[a-f0-9]{64}\z", RegexOptions.CultureInvariant);
}

public sealed class RealmImageRelease
{
    public string RealmId { get; set; } = "";
    public string ProfileId { get; set; } = "";
    public long Version { get; set; }
    public string PreviousImage { get; set; } = "";
    public bool Automatic { get; set; } = true;
    public bool IsValid() => DeploymentOptions.ValidId(RealmId) && DeploymentOptions.ValidId(ProfileId) &&
        Version > 0 && DeploymentOptions.ValidImage(PreviousImage);
}

public sealed class DeploymentTarget
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Namespace { get; set; } = "";
    public string? RealmId { get; set; }
}

public sealed class DeploymentProfile
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Image { get; set; } = "";
    public DeploymentServer[] Servers { get; set; } = [];
    public string? EnvironmentSecretName { get; set; }
    public string? ImagePullSecretName { get; set; }
    public string? TopologyTemplateServerId { get; set; }
    public string? TopologyPublicEndpointTemplate { get; set; }

    public bool IsValid() => DeploymentOptions.ValidId(Id) && DeploymentOptions.ValidName(DisplayName) &&
        DeploymentOptions.ValidImage(Image) &&
        DeploymentOptions.ValidSecret(EnvironmentSecretName) && DeploymentOptions.ValidSecret(ImagePullSecretName) &&
        Servers is { Length: >= 1 and <= 16 } && Servers.All(server => server is not null && server.IsValid()) &&
        Servers.Select(server => server.Id).Distinct().Count() == Servers.Length &&
        Servers.Select(server => server.Port).Distinct().Count() == Servers.Length &&
        RealmTopologyBuilder.ValidTemplate(this);

    public ProfileSnapshot Snapshot() => new(Id, Image,
        Servers.Select(server => new ServerDefinition(server.Id, server.Port, server.Args.ToArray())
        { Transport = server.Transport, PublicEndpoint = server.PublicEndpoint, ProtocolVersion = server.ProtocolVersion, BuildId = server.BuildId }).ToArray(),
        string.IsNullOrEmpty(EnvironmentSecretName) ? null : EnvironmentSecretName,
        string.IsNullOrEmpty(ImagePullSecretName) ? null : ImagePullSecretName);
}

public sealed class DeploymentServer
{
    public string Id { get; set; } = "";
    public int Port { get; set; }
    public string[] Args { get; set; } = [];
    public string Transport { get; set; } = "Udp";
    public string? PublicEndpoint { get; set; }
    public int ProtocolVersion { get; set; } = Arkya.ServiceDefaults.GameProtocol.Version;
    public string BuildId { get; set; } = Arkya.ServiceDefaults.GameProtocol.BuildId;
    public bool IsValid() => DeploymentOptions.ValidId(Id) && Port is >= 1024 and <= 65535 &&
        (Transport == "Udp" && string.IsNullOrEmpty(PublicEndpoint) || Transport == "WebSocket" &&
            Arkya.ServiceDefaults.GameProtocol.ValidEndpoint(PublicEndpoint) && Arkya.ServiceDefaults.GameProtocol.IsSupported(ProtocolVersion, BuildId)) &&
        Args is { Length: <= 64 } && Args.All(arg => arg is not null && arg.Length <= 512 && !arg.Contains('\0'));
}
