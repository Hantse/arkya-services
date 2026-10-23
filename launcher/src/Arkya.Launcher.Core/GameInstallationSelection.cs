namespace Arkya.Launcher.Core;

public sealed class GameInstallationSelection
{
    private readonly ReleaseClient releases;
    private readonly LauncherPreferencesStore store;
    private readonly string[] protectedDirectories;

    public LauncherPreferences Preferences { get; private set; }
    public GameInstaller Installer { get; private set; }
    public InstalledGame? Installed => Installer.ReadInstalled();

    public GameInstallationSelection(ReleaseClient releases, LauncherPreferencesStore store,
        LauncherPreferences preferences, params string[] protectedDirectories)
    {
        this.releases = releases;
        this.store = store;
        this.protectedDirectories = protectedDirectories.Append(store.DirectoryPath).ToArray();
        var path = GameInstallLocation.Validate(preferences.GameDirectory, this.protectedDirectories);
        Preferences = preferences with { GameDirectory = path };
        Installer = new GameInstaller(path, releases);
    }

    public bool SelectDirectory(string? directory)
    {
        if (directory is null) return false;
        var path = GameInstallLocation.Prepare(directory, protectedDirectories);
        var candidate = new GameInstaller(path, releases);
        _ = candidate.ReadInstalled();
        var preferences = Preferences with { GameDirectory = path, GameDirectoryConfirmed = true };
        store.Save(preferences);
        Preferences = preferences;
        Installer = candidate;
        return true;
    }

    public void SaveManifestUrl(string address)
    {
        var preferences = Preferences with { ManifestUrl = address };
        store.Save(preferences);
        Preferences = preferences;
    }
}
