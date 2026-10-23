using System.Text.Json;
using System.Text.Json.Serialization;

namespace Arkya.Launcher.Core;

public sealed record LauncherPreferences(ReleaseChannel Channel, string GameDirectory, bool GameDirectoryConfirmed = false);

public sealed class LauncherPreferencesStore(string directory, string legacyGameDirectory)
{
    private static readonly JsonSerializerOptions PreferencesJsonOptions = new(GameManifest.JsonOptions)
    {
        Converters = { new JsonStringEnumConverter<ReleaseChannel>(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };
    public string DirectoryPath { get; } = Path.GetFullPath(directory);
    public LauncherPreferences Defaults => new(ReleaseChannel.Beta, Path.GetFullPath(legacyGameDirectory));

    public LauncherPreferences Load()
    {
        var path = SafePaths.Resolve(DirectoryPath, "launcher-preferences.json");
        if (File.Exists(path))
        {
            var saved = Read<LauncherPreferences>(path);
            Validate(saved);
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.TryGetProperty("manifestUrl", out _)) Save(saved);
            return saved;
        }
        var legacy = SafePaths.Resolve(legacyGameDirectory, "launcher-preferences.json");
        var hasInstallation = File.Exists(SafePaths.Resolve(legacyGameDirectory, "current.json"));
        var preferences = Defaults with { GameDirectoryConfirmed = hasInstallation };
        if (File.Exists(legacy) || hasInstallation) Save(preferences);
        return preferences;
    }

    public void Save(LauncherPreferences preferences)
    {
        Validate(preferences);
        SafePaths.RejectReparsePoints(DirectoryPath);
        Directory.CreateDirectory(DirectoryPath);
        var temporary = SafePaths.Resolve(DirectoryPath, "preferences-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(output, preferences, PreferencesJsonOptions);
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
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), PreferencesJsonOptions)
            ?? throw new InvalidDataException("Préférences du launcher invalides.");
    }

    private static void Validate(LauncherPreferences preferences)
    {
        _ = ReleaseChannels.GetId(preferences.Channel);
        if (string.IsNullOrWhiteSpace(preferences.GameDirectory) || !Path.IsPathFullyQualified(preferences.GameDirectory))
            throw new InvalidDataException("Dossier du jeu invalide dans les préférences.");
    }
}
