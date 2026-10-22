using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
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
    private readonly CancellationTokenSource windowLifetime = new();
    private readonly ContentFeedClient publications;
    private IReadOnlyList<PostCard> posts = [];
    private string selectedKind = "all";
    private bool loadingFeed;
    private bool feedFailed;
    private string feedMessage = "Les nouvelles de l’équipe Arkya apparaîtront ici.";

    public MainWindow()
    {
        InitializeComponent();
        releases = new ReleaseClient(httpClient);
        publications = new ContentFeedClient(httpClient);
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
        var feed = RefreshFeedAsync();
        if (!string.IsNullOrWhiteSpace(ManifestAddress.Text))
        {
            SettingsPanel.IsExpanded = false;
            await CheckAsync();
        }
        else SettingsOverlay.Visibility = Visibility.Visible;
        await feed;
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
        InstallButton.Visibility = IsCurrent ? Visibility.Collapsed : Visibility.Visible;
        PlayButton.Content = GameRunning ? "Jeu en cours" : "Jouer";
        PlayButton.IsEnabled = !busy && !GameRunning && installed is not null;
        PlayButton.Visibility = installed is null ? Visibility.Collapsed : Visibility.Visible;
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

    private async Task RefreshFeedAsync()
    {
        if (loadingFeed || windowLifetime.IsCancellationRequested) return;
        loadingFeed = true;
        feedFailed = false;
        var requestedKind = selectedKind;
        feedMessage = "Chargement des publications…";
        RenderFeed();
        try
        {
            var address = "https://api.arkya.online/v1/content/posts?language=fr&limit=12" +
                (requestedKind == "all" ? "" : "&kind=" + requestedKind);
            var response = await publications.GetPostsAsync(address, windowLifetime.Token);
            posts = response.Select(post => new PostCard(post)).ToArray();
            feedMessage = "Aucune publication dans cette rubrique pour le moment. Retrouvez bientôt les nouvelles de l’équipe Arkya.";
        }
        catch (OperationCanceledException) when (windowLifetime.IsCancellationRequested) { return; }
        catch (ObjectDisposedException) when (windowLifetime.IsCancellationRequested) { return; }
        catch (Exception exception) when (IsExpected(exception) || exception is OperationCanceledException)
        {
            feedFailed = true;
            feedMessage = "Les publications sont momentanément indisponibles. Vous pouvez continuer à installer et lancer le jeu.";
        }
        finally { loadingFeed = false; }
        if (!windowLifetime.IsCancellationRequested) RenderFeed();
        if (requestedKind != selectedKind) await RefreshFeedAsync();
    }

    private void RenderFeed()
    {
        if (NewsList is null) return;
        var visible = posts.Where(post => selectedKind == "all" || post.Post.Kind == selectedKind).ToArray();
        NewsList.ItemsSource = visible;
        FeedHeading.Text = selectedKind switch { "news" => "Actualités", "release-note" => "Notes de version", _ => "À la une" };
        FeedStatus.Text = feedMessage;
        FeedStatus.Visibility = visible.Length == 0 || loadingFeed || feedFailed ? Visibility.Visible : Visibility.Collapsed;
        Hero.Visibility = selectedKind == "all" ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void FeedTab_Checked(object sender, RoutedEventArgs e)
    {
        selectedKind = (sender as RadioButton)?.Tag as string ?? "all";
        if (ArticlePanel is not null) ArticlePanel.Visibility = Visibility.Collapsed;
        if (SettingsOverlay is not null) SettingsOverlay.Visibility = Visibility.Collapsed;
        RenderFeed();
        if (IsLoaded) await RefreshFeedAsync();
    }

    private void Post_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not PostCard card) return;
        ArticleCaption.Text = card.Caption;
        ArticleTitle.Text = card.Title;
        ArticleSummary.Text = card.Summary;
        ArticleBody.Text = card.Post.Body;
        ArticlePanel.Visibility = Visibility.Visible;
    }

    private void CloseArticle_Click(object sender, RoutedEventArgs e) => ArticlePanel.Visibility = Visibility.Collapsed;
    private void Home_Click(object sender, RoutedEventArgs e)
    {
        HomeTab.IsChecked = true;
        ArticlePanel.Visibility = Visibility.Collapsed;
        SettingsOverlay.Visibility = Visibility.Collapsed;
    }
    private void Settings_Click(object sender, RoutedEventArgs e) => SettingsOverlay.Visibility = Visibility.Visible;
    private void CloseSettings_Click(object sender, RoutedEventArgs e) => SettingsOverlay.Visibility = Visibility.Collapsed;
    private async void RefreshFeed_Click(object sender, RoutedEventArgs e) => await RefreshFeedAsync();
    private void Website_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("https://arkya.online/") { UseShellExecute = true }); }
        catch (Exception exception) when (IsExpected(exception)) { ShowError(exception); }
    }
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (operation is not null)
        {
            e.Cancel = true;
            closeAfterCancellation = true;
            operation.Cancel();
            return;
        }
        windowLifetime.Cancel();
        httpClient.Dispose();
        game?.Dispose();
    }

    private sealed record LauncherSettings(string ManifestUrl);
    private sealed record PostCard(ContentPost Post)
    {
        public string Title => Post.Title;
        public string Summary => Post.Summary;
        public string Caption => (Post.Kind == "release-note" ? "MISE À JOUR " + Post.GameVersion : "ACTUALITÉ") +
            "  ·  " + Post.PublishedAt.ToLocalTime().ToString("dd MMM yyyy", CultureInfo.GetCultureInfo("fr-FR"));
    }
}
