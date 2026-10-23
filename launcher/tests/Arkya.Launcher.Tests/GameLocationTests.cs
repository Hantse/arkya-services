using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Arkya.Launcher.Core;

namespace Arkya.Launcher.Tests;

public sealed class GameLocationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "arkya-location-tests-" + Guid.NewGuid().ToString("N"));
    private readonly HttpClient http;
    private readonly ReleaseClient releases;
    private readonly byte[] package;
    private readonly GameManifest manifest;

    public GameLocationTests()
    {
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
            foreach (var name in new[] { "Arkya.exe", "UnityPlayer.dll", "Arkya_Data/data.bin" })
            {
                using var output = zip.CreateEntry(name).Open();
                output.Write(Encoding.UTF8.GetBytes("fixture"));
            }
        package = bytes.ToArray();
        manifest = new(1, "fixture-1", "win-x64", "https://releases.example.test/game.zip", package.Length,
            21, Convert.ToHexString(SHA256.HashData(package)), "Arkya.exe", "Synthetic fixture; never executed.");
        http = new HttpClient(new PackageHandler(package));
        releases = new ReleaseClient(http);
    }

    [Fact]
    public void FirstLaunchRequiresDirectoryConfirmationEvenAfterCheckingForUpdates()
    {
        var store = Store();
        var selection = Selection(store, store.Load());
        Assert.False(selection.Preferences.GameDirectoryConfirmed);
        Assert.Null(selection.Installed);
        selection.SelectChannel(ReleaseChannel.Stable);
        Assert.False(store.Load().GameDirectoryConfirmed);
        Assert.Equal(ReleaseChannel.Stable, store.Load().Channel);
    }

    [Fact]
    public async Task InstallsIntoChosenLocationAndReopensThatInstallation()
    {
        var store = Store();
        var selection = Selection(store, store.Load());
        var destination = Path.Combine(root, "custom games", "Arkya");
        Assert.True(selection.SelectDirectory(destination));
        var installed = await selection.Installer.InstallAsync(manifest, null, CancellationToken.None);
        var reopened = Selection(Store(), Store().Load());
        Assert.Equal(Path.GetFullPath(destination), reopened.Installer.Root);
        Assert.Equal(installed, reopened.Installed);
        Assert.True(reopened.Preferences.GameDirectoryConfirmed);
        Assert.Equal(ReleaseChannel.Beta, reopened.Preferences.Channel);
        Assert.True(File.Exists(Path.Combine(destination, installed.DirectoryName, "Arkya.exe")));
        Assert.False(File.Exists(Path.Combine(store.Defaults.GameDirectory, "current.json")));
    }

    [Fact]
    public async Task MigratesLegacyPreferencesAndKeepsExistingGamePlayable()
    {
        var store = Store();
        var legacyInstaller = new GameInstaller(store.Defaults.GameDirectory, releases);
        var installed = await legacyInstaller.InstallAsync(manifest, null, CancellationToken.None);
        var legacyPath = Path.Combine(legacyInstaller.Root, "launcher-preferences.json");
        const string legacyJson = "{\"manifestUrl\":\"https://legacy.example.test/latest.json\"}";
        File.WriteAllText(legacyPath, legacyJson);
        var preferences = store.Load();
        var selection = Selection(store, preferences);
        Assert.True(preferences.GameDirectoryConfirmed);
        Assert.Equal(ReleaseChannel.Beta, preferences.Channel);
        Assert.Equal(installed, selection.Installed);
        Assert.Equal(legacyJson, File.ReadAllText(legacyPath));
        Assert.True(File.Exists(Path.Combine(store.DirectoryPath, "launcher-preferences.json")));
    }

    [Fact]
    public async Task LegacyGameWithoutLegacyPreferencesIsDetected()
    {
        var store = Store();
        var game = await new GameInstaller(store.Defaults.GameDirectory, releases).InstallAsync(manifest, null, CancellationToken.None);
        var preferences = store.Load();
        Assert.True(preferences.GameDirectoryConfirmed);
        Assert.Equal(game, Selection(store, preferences).Installed);
        Assert.Equal(ReleaseChannel.Beta, preferences.Channel);
    }

    [Fact]
    public async Task ChangingDestinationKeepsOldGameAndCanSelectItAgain()
    {
        var store = Store();
        var selection = Selection(store, store.Load());
        var original = selection.Installer.Root;
        var game = await selection.Installer.InstallAsync(manifest, null, CancellationToken.None);
        var pointer = File.ReadAllText(Path.Combine(original, "current.json"));
        selection.SelectDirectory(Path.Combine(root, "another disk"));
        Assert.Null(selection.Installed);
        Assert.Equal(pointer, File.ReadAllText(Path.Combine(original, "current.json")));
        Assert.True(File.Exists(Path.Combine(original, game.DirectoryName, "Arkya.exe")));
        selection.SelectDirectory(original);
        Assert.Equal(game, selection.Installed);
        Assert.Equal(original, Selection(store, store.Load()).Installer.Root);
    }

    [Fact]
    public async Task ChannelsKeepSeparatePointersAndReopenIndependently()
    {
        var store = Store();
        var selection = Selection(store, store.Load());
        var destination = Path.Combine(root, "chosen");
        selection.SelectDirectory(destination);
        var beta = await selection.Installer.InstallAsync(manifest, null, CancellationToken.None);
        var betaPointer = File.ReadAllText(Path.Combine(destination, "current.json"));

        selection.SelectChannel(ReleaseChannel.Stable);
        Assert.Null(selection.Installed);
        Assert.Equal(Path.Combine(destination, "channels", "stable"), selection.Installer.Root);
        var stable = await selection.Installer.InstallAsync(manifest with { Version = "stable-1" }, null, CancellationToken.None);

        var reopened = Selection(store, store.Load());
        Assert.Equal(ReleaseChannel.Stable, reopened.Preferences.Channel);
        Assert.Equal(stable, reopened.Installed);
        Assert.Equal("stable", reopened.Installed!.Channel);
        reopened.SelectChannel(ReleaseChannel.Beta);
        Assert.Equal(beta, reopened.Installed);
        Assert.Equal("beta", reopened.Installed!.Channel);
        Assert.Equal(betaPointer, File.ReadAllText(Path.Combine(destination, "current.json")));
        Assert.Equal(stable, new GameInstaller(Path.Combine(destination, "channels", "stable"), releases,
            ReleaseChannel.Stable).ReadInstalled());
    }

    [Theory]
    [InlineData("beta")]
    [InlineData(null)]
    public async Task StableRejectsBetaAndLegacyPointersWithoutSwitchingPreferences(string? pointerChannel)
    {
        var store = Store();
        var selection = Selection(store, store.Load());
        selection.SelectDirectory(Path.Combine(root, "chosen"));
        var before = selection.Preferences;
        var beta = await selection.Installer.InstallAsync(manifest, null, CancellationToken.None);
        var stableRoot = ReleaseChannels.GetInstallationDirectory(before.GameDirectory, ReleaseChannel.Stable);
        Directory.CreateDirectory(stableRoot);
        File.WriteAllText(Path.Combine(stableRoot, "current.json"), JsonSerializer.Serialize(beta with { Channel = pointerChannel }, GameManifest.JsonOptions));

        Assert.Throws<InvalidDataException>(() => selection.SelectChannel(ReleaseChannel.Stable));
        Assert.Equal(before, selection.Preferences);
        Assert.Equal(before, store.Load());
        Assert.Equal(beta, selection.Installed);
    }

    [Fact]
    public async Task BetaRejectsAStablePointerAndLaunchBeforeStartingAnyProcess()
    {
        var store = Store();
        var selection = Selection(store, store.Load());
        var beta = await selection.Installer.InstallAsync(manifest, null, CancellationToken.None);
        File.WriteAllText(Path.Combine(selection.Installer.Root, "current.json"),
            JsonSerializer.Serialize(beta with { Channel = "stable" }, GameManifest.JsonOptions));
        Assert.Throws<InvalidDataException>(() => selection.Installer.ReadInstalled());
        Assert.Throws<InvalidDataException>(() => selection.Installer.Launch(beta with { Channel = "stable" }));
    }

    [Fact]
    public async Task ChangingFolderOnStableKeepsBothOldChannelInstallations()
    {
        var store = Store();
        var selection = Selection(store, store.Load());
        var first = Path.Combine(root, "first");
        selection.SelectDirectory(first);
        var beta = await selection.Installer.InstallAsync(manifest, null, CancellationToken.None);
        selection.SelectChannel(ReleaseChannel.Stable);
        var stable = await selection.Installer.InstallAsync(manifest with { Version = "stable-1" }, null, CancellationToken.None);
        selection.SelectDirectory(Path.Combine(root, "second"));
        Assert.Null(selection.Installed);
        Assert.Equal(ReleaseChannel.Stable, selection.Preferences.Channel);
        selection.SelectDirectory(first);
        Assert.Equal(stable, selection.Installed);
        selection.SelectChannel(ReleaseChannel.Beta);
        Assert.Equal(beta, selection.Installed);
    }

    [Fact]
    public void MigratesSavedCustomManifestToFixedBetaAndKeepsChosenFolder()
    {
        var store = Store();
        var destination = Path.Combine(root, "custom existing game");
        Directory.CreateDirectory(store.DirectoryPath);
        var path = Path.Combine(store.DirectoryPath, "launcher-preferences.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            manifestUrl = "http://untrusted.example.test/arbitrary.json",
            gameDirectory = destination,
            gameDirectoryConfirmed = true
        }));

        var migrated = store.Load();

        Assert.Equal(new LauncherPreferences(ReleaseChannel.Beta, destination, true), migrated);
        Assert.Equal(migrated, Store().Load());
        Assert.DoesNotContain("manifestUrl", File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"channel\": \"beta\"", File.ReadAllText(path));
        Assert.Equal("https://arkya.online/downloads/windows/latest-win-x64.json", ReleaseChannels.GetManifestUrl(migrated.Channel));
    }

    [Theory]
    [InlineData("\"nightly\"")]
    [InlineData("2")]
    [InlineData("null")]
    public void UnknownSavedChannelIsRejectedWithoutOverwritingPreferences(string channelJson)
    {
        var store = Store();
        Directory.CreateDirectory(store.DirectoryPath);
        var path = Path.Combine(store.DirectoryPath, "launcher-preferences.json");
        var json = "{\"channel\":" + channelJson + ",\"gameDirectory\":" + JsonSerializer.Serialize(store.Defaults.GameDirectory) + "}";
        File.WriteAllText(path, json);

        Assert.Throws<JsonException>(() => store.Load());
        Assert.Equal(json, File.ReadAllText(path));
    }

    [Fact]
    public void UnknownChannelCannotBeSelectedOrSaved()
    {
        var store = Store();
        var selection = Selection(store, store.Load());
        var before = selection.Preferences;
        Assert.Throws<InvalidDataException>(() => selection.SelectChannel((ReleaseChannel)42));
        Assert.Throws<InvalidDataException>(() => store.Save(before with { Channel = (ReleaseChannel)42 }));
        Assert.Equal(before, selection.Preferences);
        Assert.False(File.Exists(Path.Combine(store.DirectoryPath, "launcher-preferences.json")));
    }

    [Fact]
    public async Task CancellingDirectoryPickerPreservesPreferencesAndInstallation()
    {
        var store = Store();
        var selection = Selection(store, store.Load());
        selection.SelectDirectory(Path.Combine(root, "chosen"));
        var game = await selection.Installer.InstallAsync(manifest, null, CancellationToken.None);
        var before = File.ReadAllText(Path.Combine(store.DirectoryPath, "launcher-preferences.json"));
        Assert.False(selection.SelectDirectory(null));
        Assert.Equal(game, selection.Installed);
        Assert.Equal(before, File.ReadAllText(Path.Combine(store.DirectoryPath, "launcher-preferences.json")));
    }

    [Fact]
    public async Task InvalidExistingInstallationDoesNotReplaceSelectionOrPreferences()
    {
        var store = Store();
        var selection = Selection(store, store.Load());
        selection.SelectDirectory(Path.Combine(root, "chosen"));
        var original = selection.Preferences;
        var game = await selection.Installer.InstallAsync(manifest, null, CancellationToken.None);
        var invalid = Path.Combine(root, "broken");
        Directory.CreateDirectory(invalid);
        File.WriteAllText(Path.Combine(invalid, "current.json"), "{}");
        Assert.Throws<InvalidDataException>(() => selection.SelectDirectory(invalid));
        Assert.Equal(original, selection.Preferences);
        Assert.Equal(original, store.Load());
        Assert.Equal(game, selection.Installed);
    }

    [Fact]
    public void FailedPreferenceSaveDoesNotSwitchDirectory()
    {
        var store = Store();
        var selection = Selection(store, store.Load());
        var before = selection.Preferences;
        Directory.CreateDirectory(root);
        File.WriteAllText(store.DirectoryPath, "not a directory");
        Assert.Throws<IOException>(() => selection.SelectDirectory(Path.Combine(root, "new game")));
        Assert.Equal(before, selection.Preferences);
        Assert.Equal(before.GameDirectory, selection.Installer.Root);
        Assert.Equal("not a directory", File.ReadAllText(store.DirectoryPath));
    }

    [Fact]
    public void NewPreferencesTakePrecedenceOverStaleLegacySettings()
    {
        var store = Store();
        var selection = Selection(store, store.Load());
        selection.SelectDirectory(Path.Combine(root, "new game"));
        File.WriteAllText(Path.Combine(store.Defaults.GameDirectory, "launcher-preferences.json"),
            "{\"manifestUrl\":\"https://stale.example.test/latest.json\"}");
        Assert.Equal(selection.Preferences, Store().Load());
    }

    [Fact]
    public void MalformedSavedPreferencesAreReportedWithoutOverwritingThem()
    {
        var store = Store();
        Directory.CreateDirectory(store.DirectoryPath);
        var path = Path.Combine(store.DirectoryPath, "launcher-preferences.json");
        File.WriteAllText(path, "broken json");
        Assert.Throws<JsonException>(() => store.Load());
        Assert.Equal("broken json", File.ReadAllText(path));
    }

    [Theory]
    [InlineData("relative/path")]
    [InlineData("\\\\server\\game")]
    [InlineData("//server/game")]
    public void RejectsNonLocalOrRelativeLocations(string path) =>
        Assert.Throws<InvalidDataException>(() => GameInstallLocation.Validate(path));

    [Fact]
    public void RejectsDiskRootAndLauncherDirectories()
    {
        Assert.Throws<InvalidDataException>(() => GameInstallLocation.Validate(Path.GetPathRoot(root)!));
        var launcher = Path.Combine(root, "launcher");
        Assert.Throws<InvalidDataException>(() => GameInstallLocation.Validate(launcher, launcher));
        Assert.Throws<InvalidDataException>(() => GameInstallLocation.Validate(Path.Combine(launcher, "game"), launcher));
        Assert.Throws<InvalidDataException>(() => GameInstallLocation.Validate(root, launcher));
    }

    [Fact]
    public void RejectsWindowsAndProgramFilesDirectories()
    {
        if (!OperatingSystem.IsWindows()) return;
        foreach (var folder in new[] { Environment.SpecialFolder.Windows, Environment.SpecialFolder.ProgramFiles })
            Assert.Throws<InvalidDataException>(() => GameInstallLocation.Validate(Path.Combine(Environment.GetFolderPath(folder), "Arkya")));
    }

    [Fact]
    public void PreparationTestsWriteAccessWithoutLeavingProbeFiles()
    {
        var path = GameInstallLocation.Prepare(Path.Combine(root, "game"));
        Assert.True(Directory.Exists(path));
        Assert.Empty(Directory.GetFileSystemEntries(path));
    }

    private LauncherPreferencesStore Store() => new(Path.Combine(root, "preferences"), Path.Combine(root, "legacy game"));
    private GameInstallationSelection Selection(LauncherPreferencesStore store, LauncherPreferences preferences) =>
        new(releases, store, preferences, Path.Combine(root, "launcher"));

    public void Dispose()
    {
        http.Dispose();
        var verified = Path.GetFullPath(root);
        Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), verified, StringComparison.OrdinalIgnoreCase);
        if (Directory.Exists(verified)) Directory.Delete(verified, true);
    }

    private sealed class PackageHandler(byte[] package) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(package) });
    }
}
