using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Arkya.Launcher.Core;
using Microsoft.Win32;

namespace Arkya.Launcher;

public partial class MainWindow : Window
{
    private readonly HttpClient httpClient = ReleaseClient.CreateHttpClient();
    private readonly HttpClient identityHttpClient = LauncherIdentityClient.CreateHttpClient();
    private readonly LauncherIdentityClient identity;
    private readonly DispatcherTimer sessionTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private LauncherSession? session;
    private CancellationTokenSource? sessionLifetime;
    private bool updatingChannel;
    private readonly ReleaseClient releases;
    private readonly LauncherPreferencesStore preferencesStore;
    private GameInstallationSelection selection;
    private bool preferencesHealthy = true;
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

    public MainWindow() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Arkya"))
    {
    }

    internal MainWindow(string dataDirectory)
    {
        InitializeComponent();
        releases = new ReleaseClient(httpClient);
        identity = new LauncherIdentityClient(identityHttpClient);
        publications = new ContentFeedClient(httpClient);
        var arkya = Path.GetFullPath(dataDirectory);
        preferencesStore = new LauncherPreferencesStore(Path.Combine(arkya, "Launcher"), Path.Combine(arkya, "Game"));
        selection = new GameInstallationSelection(releases, preferencesStore, preferencesStore.Defaults, AppContext.BaseDirectory);
        try
        {
            selection = new GameInstallationSelection(releases, preferencesStore, preferencesStore.Load(), AppContext.BaseDirectory);
            installed = selection.Installer.ReadInstalled();
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            preferencesHealthy = false;
            ShowError(exception);
        }
        UpdateChannel();
        UpdateLocation();
        UpdateButtons();
        sessionTimer.Tick += (_, _) => UpdateButtons();
        sessionTimer.Start();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) => UpdateButtons();

    private bool Authenticated => session?.IsUsableAt(DateTimeOffset.UtcNow) == true;

    private async void Check_Click(object sender, RoutedEventArgs e) => await CheckAsync();

    private Task CheckAsync() => RunOperationAsync(async token =>
    {
        available = null;
        SetStatus("Recherche de la dernière version…");
        Progress.IsIndeterminate = true;
        var manifest = await releases.GetChannelReleaseAsync(selection.Preferences.Channel, token);
        token.ThrowIfCancellationRequested();
        available = manifest;
        if (available is null)
        {
            AvailableLabel.Text = "Aucune version publiée sur ce canal";
            ReleaseNotes.Text = "Les notes de version apparaîtront à la publication de ce canal.";
            SetStatus("Aucune version Stable publiée pour le moment. La Bêta reste disponible dans son canal.");
            return;
        }
        AvailableLabel.Text = "Version disponible : " + available.Version + " · " + FormatBytes(available.PackageSize);
        ReleaseNotes.Text = string.IsNullOrWhiteSpace(available.ReleaseNotes) ? "Aucune note publiée pour cette version." : available.ReleaseNotes;
        SetStatus(IsCurrent ? "Votre jeu est à jour. Bonne aventure !" : "Une version du jeu est prête à être installée.");
    });

    private async void Channel_Checked(object sender, RoutedEventArgs e)
    {
        if (updatingChannel || !Authenticated || operation is not null || GameRunning) return;
        var channel = (sender as RadioButton)?.Tag as string == "stable" ? ReleaseChannel.Stable : ReleaseChannel.Beta;
        if (channel == selection.Preferences.Channel) return;
        try
        {
            selection.SelectChannel(channel);
            available = null;
            installed = selection.Installed;
            AvailableLabel.Text = "Recherche de la version de ce canal…";
            ReleaseNotes.Text = "Les changements de la version disponible apparaîtront ici.";
            UpdateChannel();
            UpdateLocation();
            UpdateButtons();
            await CheckAsync();
        }
        catch (Exception exception) when (IsExpected(exception)) { UpdateChannel(); ShowError(exception); }
    }

    private void UpdateChannel()
    {
        updatingChannel = true;
        var beta = selection.Preferences.Channel == ReleaseChannel.Beta;
        BetaChannel.IsChecked = beta;
        StableChannel.IsChecked = !beta;
        ChannelBadge.Text = beta ? "BÊTA" : "STABLE";
        ChannelDescription.Text = beta ? "Découvrez les versions en cours de développement." : "Les versions validées pour tous les joueurs.";
        updatingChannel = false;
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (!Authenticated || operation is not null || GameRunning || available is not { } manifest) return;
        if ((!selection.Preferences.GameDirectoryConfirmed || !preferencesHealthy) && !ChooseGameDirectory()) return;
        if (IsCurrent) return;
        await RunOperationAsync(async token =>
        {
            GameInstallLocation.Prepare(selection.Installer.Root, AppContext.BaseDirectory, preferencesStore.DirectoryPath);
            var timer = Stopwatch.StartNew();
            var progress = new Progress<InstallProgress>(value =>
            {
                if (!Authenticated || timer.ElapsedMilliseconds < 100 && value.CompletedBytes != value.TotalBytes) return;
                timer.Restart();
                Progress.IsIndeterminate = value.TotalBytes == 0;
                Progress.Value = value.Percentage;
                StatusLabel.Text = value.TotalBytes > 1
                    ? $"{value.Message} · {FormatBytes(value.CompletedBytes)} / {FormatBytes(value.TotalBytes)}"
                    : value.Message;
            });
            installed = await Task.Run(() => selection.Installer.InstallAsync(manifest, progress, token), token);
            StatusLabel.Text = "Installation terminée. Arkya est prêt à démarrer.";
        });
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        if (Authenticated || operation is not null) return;
        await RunOperationAsync(async token =>
        {
            SetStatus("Terminez la connexion Discord dans votre navigateur…");
            var connected = await identity.LoginWithDiscordAsync(uri =>
            {
                using var browser = Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            }, token);
            token.ThrowIfCancellationRequested();
            sessionLifetime = CancellationTokenSource.CreateLinkedTokenSource(windowLifetime.Token);
            session = connected;
            SetStatus("Connexion établie. Bienvenue sur Arkya.");
        }, requiresSession: false);
        if (!Authenticated || closeAfterCancellation) return;
        var feed = RefreshFeedAsync();
        await CheckAsync();
        await feed;
    }

    private async void Logout_Click(object sender, RoutedEventArgs e)
    {
        if (operation is not null || session is not { } current) return;
        EndSession();
        UpdateButtons();
        await RunOperationAsync(async token =>
        {
            SetStatus("Déconnexion…");
            try
            {
                await identity.LogoutAsync(current, token);
                SetStatus("Vous êtes déconnecté. Connectez-vous avec Discord pour revenir au launcher.");
            }
            catch (Exception exception) when (IsExpected(exception) || exception is OperationCanceledException)
            {
                SetStatus("Vous êtes déconnecté du launcher. La révocation sur le serveur n’a pas pu être confirmée.");
            }
        }, requiresSession: false);
    }

    private void EndSession()
    {
        session = null;
        sessionLifetime?.Cancel();
        sessionLifetime?.Dispose();
        sessionLifetime = null;
        available = null;
        posts = [];
        NewsList.ItemsSource = null;
        ArticlePanel.Visibility = Visibility.Collapsed;
        SettingsOverlay.Visibility = Visibility.Collapsed;
        ArticleCaption.Text = ArticleTitle.Text = ArticleSummary.Text = ArticleBody.Text = "";
        AvailableLabel.Text = "Vérifiez les mises à jour après la connexion.";
        ReleaseNotes.Text = "Les changements de la version disponible apparaîtront ici.";
    }

    private async void Play_Click(object sender, RoutedEventArgs e)
    {
        if (!Authenticated || installed is not { } currentInstall || GameRunning || session is not { } currentSession) return;
        await RunOperationAsync(async token =>
        {
            StatusLabel.Text = "Vérification de votre session…";
            session = await identity.VerifySessionAsync(currentSession, token);
            token.ThrowIfCancellationRequested();
            using var handoff = currentInstall.Manifest.LauncherSessionProtocol == 1 ? new LauncherSessionHandoff() : null;
            game?.Dispose();
            var launched = handoff is null ? selection.Installer.Launch(currentInstall) : selection.Installer.Launch(currentInstall, handoff.PipeName);
            game = launched;
            launched.EnableRaisingEvents = true;
            launched.Exited += (_, _) =>
            {
                if (windowLifetime.IsCancellationRequested || Dispatcher.HasShutdownStarted) return;
                Dispatcher.BeginInvoke(() =>
                {
                    if (game != launched) return;
                    StatusLabel.Text = "Le jeu est fermé. À bientôt sur Arkya.";
                    UpdateButtons();
                });
            };
            StatusLabel.Text = "Démarrage d’Arkya…";
            if (handoff is not null) await handoff.SendAsync(launched.Id, session, token);
            StatusLabel.Text = "Arkya est en cours d’exécution.";
        });
    }

    private async Task RunOperationAsync(Func<CancellationToken, Task> action, bool requiresSession = true)
    {
        if (operation is not null || requiresSession && !Authenticated) return;
        using var currentOperation = requiresSession && sessionLifetime is not null
            ? CancellationTokenSource.CreateLinkedTokenSource(windowLifetime.Token, sessionLifetime.Token)
            : CancellationTokenSource.CreateLinkedTokenSource(windowLifetime.Token);
        operation = currentOperation;
        UpdateButtons();
        try { await action(currentOperation.Token); }
        catch (OperationCanceledException)
        {
            if (!requiresSession || Authenticated)
                SetStatus(currentOperation.IsCancellationRequested ? "Opération annulée." : "Le serveur ou le jeu n’a pas répondu à temps. Vous pouvez réessayer.");
        }
        catch (LauncherAuthenticationException exception)
        {
            if (exception.SessionInvalid) EndSession();
            ShowError(exception);
        }
        catch (Exception exception) when (IsExpected(exception)) { ShowError(exception); }
        finally
        {
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
        if (session is not null && !Authenticated)
        {
            EndSession();
            SetStatus("Votre session a expiré. Reconnectez-vous avec Discord pour continuer.");
        }
        var busy = operation is not null;
        var connected = Authenticated;
        LoginPage.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
        LauncherContent.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
        AccountLabel.Text = session?.DisplayName ?? "Non connecté";
        AccountLabel.ToolTip = session?.DisplayName;
        LoginButton.IsEnabled = !busy;
        LoginProgress.Visibility = !connected && busy ? Visibility.Visible : Visibility.Collapsed;
        LoginProgress.IsIndeterminate = !connected && busy;
        LoginCancelButton.Visibility = !connected && busy ? Visibility.Visible : Visibility.Collapsed;
        LogoutButton.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
        LogoutButton.IsEnabled = connected && !busy;
        InstalledLabel.Text = installed is null ? "Aucune version installée" : "Arkya " + installed.Manifest.Version;
        InstallButton.Content = IsCurrent ? "Jeu à jour" : installed is null ? "Installer le jeu" : "Mettre à jour";
        InstallButton.IsEnabled = connected && !busy && !GameRunning && available is not null && !IsCurrent;
        InstallButton.Visibility = IsCurrent ? Visibility.Collapsed : Visibility.Visible;
        PlayButton.Content = GameRunning ? "Jeu en cours" : "Jouer";
        PlayButton.IsEnabled = connected && !busy && !GameRunning && installed is not null;
        PlayButton.Visibility = installed is null ? Visibility.Collapsed : Visibility.Visible;
        CheckButton.IsEnabled = connected && !busy;
        BetaChannel.IsEnabled = StableChannel.IsEnabled = connected && !busy && !GameRunning;
        ChangeLocationButton.IsEnabled = SettingsLocationButton.IsEnabled = connected && !busy && !GameRunning;
        CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetStatus(string message) => StatusLabel.Text = LoginStatus.Text = message;

    private void ShowError(Exception exception)
    {
        SetStatus(exception is HttpRequestException
            ? "Connexion au serveur impossible. Vérifiez votre connexion et réessayez."
            : exception.Message);
    }

    private static bool IsExpected(Exception exception) => exception is IOException or UnauthorizedAccessException
        or HttpRequestException or JsonException or Win32Exception or InvalidDataException or ArgumentException or LauncherAuthenticationException;

    private static string FormatBytes(long value) => value >= 1024L * 1024 * 1024
        ? $"{value / (1024d * 1024 * 1024):0.0} Go" : $"{value / (1024d * 1024):0.0} Mo";

    private void Cancel_Click(object sender, RoutedEventArgs e) => operation?.Cancel();

    private void ChangeLocation_Click(object sender, RoutedEventArgs e) => ChooseGameDirectory();

    private bool ChooseGameDirectory()
    {
        if (!Authenticated || operation is not null || GameRunning) return false;
        try
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Choisir le dossier d’installation du jeu Arkya",
                InitialDirectory = selection.Preferences.GameDirectory,
                Multiselect = false
            };
            if (dialog.ShowDialog(this) != true) return false;
            if (!Authenticated) return false;
            selection.SelectDirectory(dialog.FolderName);
            preferencesHealthy = true;
            installed = selection.Installed;
            UpdateLocation();
            UpdateButtons();
            StatusLabel.Text = installed is null
                ? "Dossier choisi. Cliquez sur Installer le jeu pour télécharger Arkya ici. Les autres installations sont conservées."
                : "Installation existante retrouvée dans le dossier choisi.";
            return true;
        }
        catch (Exception exception) when (IsExpected(exception)) { ShowError(exception); return false; }
    }

    private void UpdateLocation()
    {
        LocationLabel.Text = selection.Installer.Root;
        LocationLabel.ToolTip = selection.Installer.Root;
        FooterLocationLabel.Text = selection.Installer.Root;
        FooterLocationLabel.ToolTip = selection.Installer.Root;
    }

    private async Task RefreshFeedAsync()
    {
        if (!Authenticated || loadingFeed || sessionLifetime is null || windowLifetime.IsCancellationRequested) return;
        var requestedLifetime = sessionLifetime;
        var token = requestedLifetime.Token;
        loadingFeed = true;
        feedFailed = false;
        var requestedKind = selectedKind;
        feedMessage = "Chargement des publications…";
        RenderFeed();
        try
        {
            var address = "https://api.arkya.online/v1/content/posts?language=fr&limit=12" +
                (requestedKind == "all" ? "" : "&kind=" + requestedKind);
            var response = await publications.GetPostsAsync(address, token);
            token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(requestedLifetime, sessionLifetime)) return;
            posts = response.Select(post => new PostCard(post)).ToArray();
            feedMessage = "Aucune publication dans cette rubrique pour le moment. Retrouvez bientôt les nouvelles de l’équipe Arkya.";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        catch (ObjectDisposedException) when (windowLifetime.IsCancellationRequested) { return; }
        catch (Exception exception) when (IsExpected(exception) || exception is OperationCanceledException)
        {
            feedFailed = true;
            feedMessage = "Les publications sont momentanément indisponibles. Vous pouvez continuer à installer et lancer le jeu.";
        }
        finally { loadingFeed = false; }
        if (!Authenticated || !ReferenceEquals(requestedLifetime, sessionLifetime)) return;
        RenderFeed();
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
        if (IsLoaded && Authenticated) await RefreshFeedAsync();
    }

    private void Post_Click(object sender, RoutedEventArgs e)
    {
        if (!Authenticated || (sender as Button)?.Tag is not PostCard card) return;
        ArticleCaption.Text = card.Caption;
        ArticleTitle.Text = card.Title;
        ArticleSummary.Text = card.Summary;
        ArticleBody.Text = card.Post.Body;
        ArticlePanel.Visibility = Visibility.Visible;
    }

    private void CloseArticle_Click(object sender, RoutedEventArgs e) => ArticlePanel.Visibility = Visibility.Collapsed;
    private void Home_Click(object sender, RoutedEventArgs e)
    {
        if (!Authenticated) return;
        HomeTab.IsChecked = true;
        ArticlePanel.Visibility = Visibility.Collapsed;
        SettingsOverlay.Visibility = Visibility.Collapsed;
    }
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (Authenticated) SettingsOverlay.Visibility = Visibility.Visible;
    }
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
        sessionTimer.Stop();
        EndSession();
        identityHttpClient.Dispose();
        httpClient.Dispose();
        game?.Dispose();
    }

    private sealed record PostCard(ContentPost Post)
    {
        public string Title => Post.Title;
        public string Summary => Post.Summary;
        public string Caption => (Post.Kind == "release-note" ? "MISE À JOUR " + Post.GameVersion : "ACTUALITÉ") +
            "  ·  " + Post.PublishedAt.ToLocalTime().ToString("dd MMM yyyy", CultureInfo.GetCultureInfo("fr-FR"));
    }
}
