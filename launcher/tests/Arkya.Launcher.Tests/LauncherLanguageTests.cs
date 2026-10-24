using System.Text.Json;
using Arkya.Launcher.Core;

namespace Arkya.Launcher.Tests;

public sealed class LauncherLanguageTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "arkya-language-tests-" + Guid.NewGuid().ToString("N"));
    private readonly HttpClient http = new(new DenyNetworkHandler());

    [Fact]
    public void FirstLaunchDefaultsToFrenchWithoutCreatingPreferences()
    {
        var store = Store();
        Assert.Equal("fr", store.Defaults.Language);
        Assert.Equal(store.Defaults, store.Load());
        Assert.False(Directory.Exists(store.DirectoryPath));
    }

    [Fact]
    public void ExistingPreferencesWithoutLanguageKeepTheirChannelAndChosenGameDirectory()
    {
        var store = Store();
        var expected = new LauncherPreferences(ReleaseChannel.Stable, Path.Combine(root, "chosen game"), true);
        var json = JsonSerializer.Serialize(new
        {
            channel = "stable",
            gameDirectory = expected.GameDirectory,
            gameDirectoryConfirmed = true
        });
        WritePreferences(store, json);

        Assert.Equal(expected, store.Load());
        Assert.Equal(json, File.ReadAllText(PreferencesPath(store)));
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("en")]
    public void SupportedLanguageRoundTripsWithTheOtherPreferences(string language)
    {
        var store = Store();
        var expected = new LauncherPreferences(ReleaseChannel.Stable, Path.Combine(root, "chosen game"), true, language);
        store.Save(expected);

        Assert.Equal(expected, Store().Load());
        using var json = JsonDocument.Parse(File.ReadAllText(PreferencesPath(store)));
        Assert.Equal(language, json.RootElement.GetProperty("language").GetString());
    }

    [Theory]
    [InlineData("\"de\"")]
    [InlineData("\"EN\"")]
    [InlineData("\"en-US\"")]
    [InlineData("\"\"")]
    [InlineData("null")]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("{}")]
    [InlineData("[]")]
    public void InvalidSavedLanguageFallsBackToFrenchWithoutLosingOrOverwritingGamePreferences(string languageJson)
    {
        var store = Store();
        var expected = new LauncherPreferences(ReleaseChannel.Stable, Path.Combine(root, "chosen game"), true);
        var json = "{\"channel\":\"stable\",\"gameDirectory\":" + JsonSerializer.Serialize(expected.GameDirectory) +
            ",\"gameDirectoryConfirmed\":true,\"language\":" + languageJson + "}";
        WritePreferences(store, json);

        Assert.Equal(expected, store.Load());
        Assert.Equal(json, File.ReadAllText(PreferencesPath(store)));
    }

    [Fact]
    public void LanguageSelectionPersistsWithoutChangingInstallationOrProtectedSession()
    {
        var store = Store();
        var original = new LauncherPreferences(ReleaseChannel.Stable, Path.Combine(root, "chosen game"), true);
        store.Save(original);
        var selection = Selection(store, original);
        var installer = selection.Installer;
        var gamePath = Path.Combine(installer.Root, "preserved-game.bin");
        byte[] gameData = [1, 2, 3];
        File.WriteAllBytes(gamePath, gameData);
        var gameFiles = Directory.GetFiles(original.GameDirectory, "*", SearchOption.AllDirectories);
        var sessionPath = Path.Combine(store.DirectoryPath, "launcher-session.dat");
        byte[] protectedSession = [0, 128, 255, 3, 17];
        File.WriteAllBytes(sessionPath, protectedSession);

        selection.SelectLanguage("en");

        Assert.Equal(original with { Language = "en" }, selection.Preferences);
        Assert.Equal(selection.Preferences, Store().Load());
        Assert.Same(installer, selection.Installer);
        Assert.Equal(protectedSession, File.ReadAllBytes(sessionPath));
        Assert.Equal(gameData, File.ReadAllBytes(gamePath));
        Assert.Equal(gameFiles, Directory.GetFiles(original.GameDirectory, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void DirectoryAndChannelSelectionsPreserveLanguage()
    {
        var store = Store();
        var selection = Selection(store, store.Defaults);
        selection.SelectLanguage("en");
        selection.SelectChannel(ReleaseChannel.Stable);
        Assert.Equal("en", Store().Load().Language);

        var destination = Path.Combine(root, "new game folder");
        Assert.True(selection.SelectDirectory(destination));
        var reopened = Selection(Store(), Store().Load());
        Assert.Equal(new LauncherPreferences(ReleaseChannel.Stable, destination, true, "en"), reopened.Preferences);
        Assert.Equal(Path.Combine(destination, "channels", "stable"), reopened.Installer.Root);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("de")]
    [InlineData("EN")]
    [InlineData("en-US")]
    public void UnsupportedLanguageCannotReplaceSelectionOrSavedPreferences(string? language)
    {
        var store = Store();
        var original = store.Defaults with { Language = "en" };
        store.Save(original);
        var selection = Selection(store, original);
        var installer = selection.Installer;
        var saved = File.ReadAllText(PreferencesPath(store));

        Assert.Throws<InvalidDataException>(() => selection.SelectLanguage(language!));
        Assert.Throws<InvalidDataException>(() => store.Save(original with { Language = language! }));
        Assert.Equal(original, selection.Preferences);
        Assert.Same(installer, selection.Installer);
        Assert.Equal(saved, File.ReadAllText(PreferencesPath(store)));
    }

    [Fact]
    public void FailedPreferenceSaveDoesNotChangeTheSelectedLanguage()
    {
        var store = Store();
        var selection = Selection(store, store.Defaults);
        var original = selection.Preferences;
        Directory.CreateDirectory(root);
        File.WriteAllText(store.DirectoryPath, "not a directory");

        Assert.Throws<IOException>(() => selection.SelectLanguage("en"));
        Assert.Equal(original, selection.Preferences);
        Assert.Equal("not a directory", File.ReadAllText(store.DirectoryPath));
    }

    private LauncherPreferencesStore Store() => new(Path.Combine(root, "preferences"), Path.Combine(root, "legacy game"));
    private GameInstallationSelection Selection(LauncherPreferencesStore store, LauncherPreferences preferences) =>
        new(new ReleaseClient(http), store, preferences, Path.Combine(root, "launcher"));
    private static string PreferencesPath(LauncherPreferencesStore store) => Path.Combine(store.DirectoryPath, "launcher-preferences.json");
    private static void WritePreferences(LauncherPreferencesStore store, string json)
    {
        Directory.CreateDirectory(store.DirectoryPath);
        File.WriteAllText(PreferencesPath(store), json);
    }

    public void Dispose()
    {
        http.Dispose();
        var verified = Path.GetFullPath(root);
        Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), verified, StringComparison.OrdinalIgnoreCase);
        if (Directory.Exists(verified)) Directory.Delete(verified, true);
    }

    private sealed class DenyNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Network access is forbidden in language preference tests.");
    }
}
