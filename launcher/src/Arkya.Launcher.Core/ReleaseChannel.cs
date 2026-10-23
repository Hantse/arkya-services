namespace Arkya.Launcher.Core;

public enum ReleaseChannel
{
    Beta,
    Stable
}

public static class ReleaseChannels
{
    public static string GetId(ReleaseChannel channel) => channel switch
    {
        ReleaseChannel.Beta => "beta",
        ReleaseChannel.Stable => "stable",
        _ => throw new InvalidDataException("Canal de distribution invalide.")
    };

    public static string GetManifestUrl(ReleaseChannel channel) => channel switch
    {
        ReleaseChannel.Beta => "https://arkya.online/downloads/windows/latest-win-x64.json",
        ReleaseChannel.Stable => "https://arkya.online/downloads/windows/latest-stable-win-x64.json",
        _ => throw new InvalidDataException("Canal de distribution invalide.")
    };

    public static string GetInstallationDirectory(string baseDirectory, ReleaseChannel channel) => channel switch
    {
        ReleaseChannel.Beta => Path.GetFullPath(baseDirectory),
        ReleaseChannel.Stable => SafePaths.Resolve(Path.GetFullPath(baseDirectory), "channels/stable"),
        _ => throw new InvalidDataException("Canal de distribution invalide.")
    };
}
