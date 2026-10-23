using System.Text.Json;

namespace Arkya.Launcher.Core;

public sealed record LauncherPreferences(string ManifestUrl, string GameDirectory, bool GameDirectoryConfirmed = false);

public sealed class LauncherPreferencesStore(string directory, string legacyGameDirectory, string defaultManifestUrl)
{
    public string DirectoryPath { get; } = Path.GetFullPath(directory);
    public LauncherPreferences Defaults => new(defaultManifestUrl, Path.GetFullPath(legacyGameDirectory));

    public LauncherPreferences Load()
    {
        var path = SafePaths.Resolve(DirectoryPath, "launcher-preferences.json");
        if (File.Exists(path)) return Read<LauncherPreferences>(path);
        var legacy = SafePaths.Resolve(legacyGameDirectory, "launcher-preferences.json");
        var hasInstallation = File.Exists(SafePaths.Resolve(legacyGameDirectory, "current.json"));
        var preferences = Defaults with { GameDirectoryConfirmed = hasInstallation };
        if (File.Exists(legacy)) preferences = preferences with { ManifestUrl = Read<LegacyPreferences>(legacy).ManifestUrl };
        if (File.Exists(legacy) || hasInstallation) Save(preferences);
        return preferences;
    }

    public void Save(LauncherPreferences preferences)
    {
        GameManifest.ValidateHttpsUrl(preferences.ManifestUrl);
        if (string.IsNullOrWhiteSpace(preferences.GameDirectory) || !Path.IsPathFullyQualified(preferences.GameDirectory))
            throw new InvalidDataException("Dossier du jeu invalide dans les préférences.");
        SafePaths.RejectReparsePoints(DirectoryPath);
        Directory.CreateDirectory(DirectoryPath);
        var temporary = SafePaths.Resolve(DirectoryPath, "preferences-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(output, preferences, GameManifest.JsonOptions);
                output.Flush(true);
            }
            var target = SafePaths.Resolve(DirectoryPath, "launcher-preferences.json");
            if (File.Exists(target)) File.Replace(temporary, target, null);
            else File.Move(temporary, target);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static T Read<T>(string path)
    {
        if (new FileInfo(path).Length > 65536) throw new InvalidDataException("Préférences du launcher invalides.");
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), GameManifest.JsonOptions)
            ?? throw new InvalidDataException("Préférences du launcher invalides.");
    }

    private sealed record LegacyPreferences(string ManifestUrl);
}
