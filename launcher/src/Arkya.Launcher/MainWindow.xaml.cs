using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using Arkya.Launcher.Core;

namespace Arkya.Launcher;

public partial class MainWindow : Window
{
    private readonly HttpClient httpClient = ReleaseClient.CreateHttpClient();
    private readonly ReleaseClient releases;
    private readonly GameInstaller installer;
    private readonly string preferencesPath;
    private GameManifest? available;
    private InstalledGame? installed;
    private CancellationTokenSource? operation;
    private Process? game;
    private bool closeAfterCancellation;

    public MainWindow()
    {
        InitializeComponent();
        releases = new ReleaseClient(httpClient);
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Arkya", "Game");
        installer = new GameInstaller(root, releases);
        preferencesPath = SafePaths.Resolve(root, "launcher-preferences.json");
        LocationLabel.Text = "Installation : " + root;
        LocationLabel.ToolTip = root;
        try
        {
            var defaults = Path.Combine(AppContext.BaseDirectory, "launcher-settings.json");
            var settings = File.Exists(preferencesPath) ? preferencesPath : defaults;
            ManifestAddress.Text = Environment.GetEnvironmentVariable("ARKYA_LAUNCHER_MANIFEST_URL") ??
                (File.Exists(settings) ? JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(settings), GameManifest.JsonOptions)?.ManifestUrl : "") ?? "";
            installed = installer.ReadInstalled();
        }
        catch (Exception exception) when (IsExpected(exception)) { ShowError(exception); }
        ManifestAddress.TextChanged += (_, _) =>
        {
            available = null;
            AvailableLabel.Text = "Vérifiez ce canal pour connaître la version disponible.";
            UpdateButtons();
        };
        UpdateButtons();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (Environment.GetCommandLineArgs().Contains("--smoke-test")) return;
        if (!string.IsNullOrWhiteSpace(ManifestAddress.Text))
        {
            SettingsPanel.IsExpanded = false;
            await CheckAsync();
        }
    }

    private async void Check_Click(object sender, RoutedEventArgs e) => await CheckAsync();

    private Task CheckAsync() => RunOperationAsync(async token =>
    {
        available = null;
        var address = ManifestAddress.Text.Trim();
        GameManifest.ValidateHttpsUrl(address);
        StatusLabel.Text = "Recherche de la dernière version…";
        Progress.IsIndeterminate = true;
        available = await releases.GetManifestAsync(address, token);
        await File.WriteAllTextAsync(preferencesPath, JsonSerializer.Serialize(new LauncherSettings(address), GameManifest.JsonOptions), token);
        AvailableLabel.Text = "Version disponible : " + available.Version + " · " + FormatBytes(available.PackageSize);
        ReleaseNotes.Text = string.IsNullOrWhiteSpace(available.ReleaseNotes) ? "Aucune note publiée pour cette version." : available.ReleaseNotes;
        StatusLabel.Text = IsCurrent ? "Votre jeu est à jour. Bonne aventure !" : "Une version du jeu est prête à être installée.";
    });

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (available is not { } manifest) return;
        await RunOperationAsync(async token =>
        {
            var timer = Stopwatch.StartNew();
            var progress = new Progress<InstallProgress>(value =>
            {
                if (timer.ElapsedMilliseconds < 100 && value.CompletedBytes != value.TotalBytes) return;
                timer.Restart();
                Progress.IsIndeterminate = value.TotalBytes == 0;
                Progress.Value = value.Percentage;
                StatusLabel.Text = value.TotalBytes > 1
                    ? $"{value.Message} · {FormatBytes(value.CompletedBytes)} / {FormatBytes(value.TotalBytes)}"
                    : value.Message;
            });
            installed = await Task.Run(() => installer.InstallAsync(manifest, progress, token), token);
            StatusLabel.Text = "Installation terminée. Arkya est prêt à démarrer.";
        });
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (installed is null || GameRunning) return;
        try
        {
            game = installer.Launch(installed);
            game.EnableRaisingEvents = true;
            game.Exited += (_, _) => Dispatcher.BeginInvoke(() =>
            {
                StatusLabel.Text = "Votre session est terminée. À bientôt sur Arkya.";
                UpdateButtons();
            });
            StatusLabel.Text = "Arkya est en cours d’exécution.";
            UpdateButtons();
        }
        catch (Exception exception) when (IsExpected(exception)) { ShowError(exception); }
    }

    private async Task RunOperationAsync(Func<CancellationToken, Task> action)
    {
        if (operation is not null) return;
        operation = new CancellationTokenSource();
        UpdateButtons();
        try { await action(operation.Token); }
        catch (OperationCanceledException)
        {
            StatusLabel.Text = operation.IsCancellationRequested
                ? "Opération annulée. Votre version installée est conservée."
                : "Le serveur n’a pas répondu à temps. Vous pouvez réessayer.";
        }
        catch (Exception exception) when (IsExpected(exception)) { ShowError(exception); }
        finally
        {
            operation.Dispose();
            operation = null;
            Progress.IsIndeterminate = false;
            UpdateButtons();
            if (closeAfterCancellation) Close();
        }
    }

    private bool IsCurrent => installed is not null && available is not null &&
        string.Equals(installed.Manifest.Sha256, available.Sha256, StringComparison.OrdinalIgnoreCase);
    private bool GameRunning => game is not null && !game.HasExited;

    private void UpdateButtons()
    {
        var busy = operation is not null;
        InstalledLabel.Text = installed is null ? "Aucune version installée" : "Arkya " + installed.Manifest.Version;
        InstallButton.Content = IsCurrent ? "Jeu à jour" : installed is null ? "Installer le jeu" : "Mettre à jour";
        InstallButton.IsEnabled = !busy && !GameRunning && available is not null && !IsCurrent;
        PlayButton.Content = GameRunning ? "Jeu en cours" : "Jouer";
        PlayButton.IsEnabled = !busy && !GameRunning && installed is not null;
        CheckButton.IsEnabled = !busy;
        ManifestAddress.IsEnabled = !busy;
        CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowError(Exception exception)
    {
        StatusLabel.Text = exception is HttpRequestException
            ? "Connexion au serveur de distribution impossible. Vérifiez votre connexion et réessayez."
            : exception.Message;
    }

    private static bool IsExpected(Exception exception) => exception is IOException or UnauthorizedAccessException
        or HttpRequestException or JsonException or Win32Exception or InvalidDataException or ArgumentException;

    private static string FormatBytes(long value) => value >= 1024L * 1024 * 1024
        ? $"{value / (1024d * 1024 * 1024):0.0} Go" : $"{value / (1024d * 1024):0.0} Mo";

    private void Cancel_Click(object sender, RoutedEventArgs e) => operation?.Cancel();

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (operation is not null)
        {
            e.Cancel = true;
            closeAfterCancellation = true;
            operation.Cancel();
            return;
        }
        httpClient.Dispose();
        game?.Dispose();
    }

    private sealed record LauncherSettings(string ManifestUrl);
}
