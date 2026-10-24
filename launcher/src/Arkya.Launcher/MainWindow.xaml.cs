using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
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
    private readonly LauncherUpdateClient launcherUpdates;
    private readonly string launcherDataDirectory;
    private readonly bool automaticServices;
    private LauncherUpdateManifest? launcherUpdate;
    private bool checkingLauncherUpdate;
    private bool refreshingSession;
    private DateTimeOffset nextSessionAttempt;
    private DateTimeOffset nextLauncherCheck;
    private IReadOnlyList<PostCard> posts = [];
    private string selectedKind = "all";
    private bool loadingFeed;
    private bool feedFailed;
    private Func<string>? feedMessage;
    private CancellationTokenSource? feedRequest;
    private long feedGeneration;

    public MainWindow() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Arkya"), true)
    {
    }

    internal MainWindow(string dataDirectory) : this(dataDirectory, false)
    {
    }

    internal MainWindow(string dataDirectory, bool automaticServices)
    {
        InitializeComponent();
        releases = new ReleaseClient(httpClient);
        publications = new ContentFeedClient(httpClient);
        var arkya = Path.GetFullPath(dataDirectory);
        this.automaticServices = automaticServices;
        launcherDataDirectory = Path.Combine(arkya, "Launcher");
        identity = new LauncherIdentityClient(identityHttpClient, sessionStore: new ProtectedLauncherSessionStore(launcherDataDirectory));
        launcherUpdates = new LauncherUpdateClient(httpClient);
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
        UpdateLanguageResources();
        ApplyLanguage();
        UpdateChannel();
        UpdateLocation();
        UpdateButtons();
        sessionTimer.Tick += async (_, _) => await MaintainConnectionAsync();
        sessionTimer.Start();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateButtons();
        if (!automaticServices) return;
        var updateCheck = CheckLauncherUpdateAsync();
        await RestoreConnectionAsync();
        await updateCheck;
    }

    private void AcceptSession(LauncherSession connected)
    {
        sessionLifetime ??= CancellationTokenSource.CreateLinkedTokenSource(windowLifetime.Token);
        session = connected;
        nextSessionAttempt = DateTimeOffset.MinValue;
    }

    private async Task RestoreConnectionAsync()
    {
        if (operation is not null || refreshingSession || windowLifetime.IsCancellationRequested) return;
        nextSessionAttempt = DateTimeOffset.UtcNow.AddMinutes(1);
        await RunOperationAsync(async token =>
        {
            var restored = await identity.RestoreSessionAsync(token);
            token.ThrowIfCancellationRequested();
            if (restored is null) return;
            AcceptSession(restored);
            SetStatus(() => T("Connexion retrouvée. Bienvenue sur Arkya.", "Your connection was restored. Welcome to Arkya."));
        }, requiresSession: false);
        if (Authenticated && !closeAfterCancellation)
        {
            var feed = RefreshFeedAsync();
            await CheckAsync();
            await feed;
        }
    }

    private async Task MaintainConnectionAsync()
    {
        if (!automaticServices || windowLifetime.IsCancellationRequested) { UpdateButtons(); return; }
        if (refreshingSession) return;
        if (session is { } current && current.ExpiresAt <= DateTimeOffset.UtcNow.AddMinutes(1) && DateTimeOffset.UtcNow >= nextSessionAttempt)
        {
            refreshingSession = true;
            nextSessionAttempt = DateTimeOffset.UtcNow.AddSeconds(30);
            UpdateButtons();
            try
            {
                var renewed = await identity.RefreshSessionAsync(current, windowLifetime.Token);
                if (ReferenceEquals(session, current)) AcceptSession(renewed);
            }
            catch (OperationCanceledException) when (windowLifetime.IsCancellationRequested) { return; }
            catch (LauncherAuthenticationException exception) when (exception.SessionInvalid)
            {
                EndSession();
                await DiscardSavedSessionAsync();
                ShowError(exception);
            }
            catch (Exception exception) when (IsExpected(exception) || exception is OperationCanceledException)
            {
                SetStatus(() => T("Le renouvellement de connexion est momentanément indisponible. Nouvelle tentative automatique…", "Your connection could not be renewed right now. Retrying automatically…"));
            }
            finally
            {
                refreshingSession = false;
                if (closeAfterCancellation && operation is null) Close();
            }
        }
        UpdateButtons();
        if (session is null && operation is null && DateTimeOffset.UtcNow >= nextSessionAttempt) await RestoreConnectionAsync();
        if (DateTimeOffset.UtcNow >= nextLauncherCheck) await CheckLauncherUpdateAsync();
    }

    private async Task CheckLauncherUpdateAsync()
    {
        if (checkingLauncherUpdate || windowLifetime.IsCancellationRequested) return;
        checkingLauncherUpdate = true;
        nextLauncherCheck = DateTimeOffset.UtcNow.AddMinutes(30);
        try
        {
            var currentVersion = typeof(MainWindow).Assembly.GetName().Version ?? new Version(0, 4, 0);
            launcherUpdate = await launcherUpdates.GetUpdateAsync(currentVersion, windowLifetime.Token);
            if (launcherUpdate is not { } update) return;
            SetLauncherUpdateText(() => "Arkya Launcher " + update.Version +
                T(" est disponible. Votre connexion et vos jeux seront conservés.", " is available. Your sign-in and games will be kept."));
            UpdateLauncherUpdateButton();
            LauncherUpdateBanner.Visibility = Visibility.Visible;
        }
        catch (Exception exception) when (IsExpected(exception) || exception is OperationCanceledException)
        {
            nextLauncherCheck = DateTimeOffset.UtcNow.AddMinutes(5);
        }
        finally { checkingLauncherUpdate = false; UpdateButtons(); }
    }

    private async void LauncherUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (launcherUpdate is not { } update || operation is not null || refreshingSession || GameRunning) return;
        if (!LauncherUpdateInstaller.IsInstalledLauncher(AppContext.BaseDirectory))
        {
            WebsiteDownload();
            return;
        }
        await RunOperationAsync(async token =>
        {
            var directory = Path.Combine(launcherDataDirectory, "Updates", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var installer = Path.Combine(directory, "Arkya-Launcher-Setup.exe");
            var progress = new Progress<InstallProgress>(value =>
                SetLauncherUpdateText(() => T("Mise à jour du launcher : ", "Launcher update: ") +
                    LocalizeProgress(value.Message) + " · " + FormatBytes(value.CompletedBytes)));
            await launcherUpdates.DownloadAsync(update, installer, progress, token);
            token.ThrowIfCancellationRequested();
            LauncherUpdateInstaller.StartAfterLauncherExit(update, installer, AppContext.BaseDirectory);
            closeAfterCancellation = true;
        }, requiresSession: false);
    }

    private void WebsiteDownload()
    {
        try { Process.Start(new ProcessStartInfo("https://arkya.online/play.html") { UseShellExecute = true }); }
        catch (Exception exception) when (IsExpected(exception)) { ShowError(exception); }
    }

    private bool Authenticated => session?.IsUsableAt(DateTimeOffset.UtcNow) == true;

    private async void Check_Click(object sender, RoutedEventArgs e) => await CheckAsync();

    private Task CheckAsync() => RunOperationAsync(async token =>
    {
        available = null;
        SetStatus(() => T("Recherche de la dernière version…", "Checking for the latest version…"));
        Progress.IsIndeterminate = true;
        var manifest = await releases.GetChannelReleaseAsync(selection.Preferences.Channel, token);
        token.ThrowIfCancellationRequested();
        available = manifest;
        if (manifest is null)
        {
            SetAvailableText(() => T("Aucune version publiée sur ce canal", "No release is available in this channel"));
            SetReleaseNotesText(() => T("Les notes de version apparaîtront à la publication de ce canal.", "Release notes will appear when this channel is published."));
            SetStatus(() => T("Aucune version Stable publiée pour le moment. La Bêta reste disponible dans son canal.", "No Stable release is available yet. Beta remains available in its channel."));
            return;
        }
        SetAvailableText(() => T("Version disponible : ", "Available version: ") + manifest.Version + " · " + FormatBytes(manifest.PackageSize));
        SetReleaseNotesText(() => string.IsNullOrWhiteSpace(manifest.ReleaseNotes) ? T("Aucune note publiée pour cette version.", "No notes were published for this version.") : manifest.ReleaseNotes);
        SetStatus(() => IsCurrent ? T("Votre jeu est à jour. Bonne aventure !", "Your game is up to date. Enjoy your adventure!") : T("Une version du jeu est prête à être installée.", "A game release is ready to install."));
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
            SetAvailableText(() => T("Recherche de la version de ce canal…", "Checking this channel’s latest release…"));
            SetReleaseNotesText(() => T("Les changements de la version disponible apparaîtront ici.", "Changes in the available release will appear here."));
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
        ChannelBadge.Text = beta ? T("BÊTA", "BETA") : "STABLE";
        ChannelDescription.Text = beta ? T("Découvrez les versions en cours de développement.", "Explore releases currently in development.") : T("Les versions validées pour tous les joueurs.", "Releases approved for all players.");
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
                SetStatus(() => value.TotalBytes > 1
                    ? $"{LocalizeProgress(value.Message)} · {FormatBytes(value.CompletedBytes)} / {FormatBytes(value.TotalBytes)}"
                    : LocalizeProgress(value.Message));
            });
            installed = await Task.Run(() => selection.Installer.InstallAsync(manifest, progress, token), token);
            SetStatus(() => T("Installation terminée. Arkya est prêt à démarrer.", "Installation complete. Arkya is ready to launch."));
        });
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        if (Authenticated || operation is not null) return;
        await RunOperationAsync(async token =>
        {
            SetStatus(() => T("Terminez la connexion Discord dans votre navigateur…", "Complete your Discord sign-in in your browser…"));
            var connected = await identity.LoginWithDiscordAsync(uri =>
            {
                using var browser = Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            }, token);
            token.ThrowIfCancellationRequested();
            AcceptSession(connected);
            SetStatus(() => T("Connexion établie. Bienvenue sur Arkya.", "You are signed in. Welcome to Arkya."));
        }, requiresSession: false);
        if (!Authenticated || closeAfterCancellation) return;
        var feed = RefreshFeedAsync();
        await CheckAsync();
        await feed;
    }

    private async void Logout_Click(object sender, RoutedEventArgs e)
    {
        if (operation is not null || refreshingSession || session is not { } current) return;
        EndSession();
        UpdateButtons();
        await RunOperationAsync(async token =>
        {
            SetStatus(() => T("Déconnexion…", "Signing out…"));
            try
            {
                await identity.LogoutAsync(current, token);
                SetStatus(() => T("Vous êtes déconnecté. Connectez-vous avec Discord pour revenir au launcher.", "You are signed out. Sign in with Discord to return to the launcher."));
            }
            catch (Exception exception) when (IsExpected(exception) || exception is OperationCanceledException)
            {
                SetStatus(() => T("Vous êtes déconnecté du launcher. La révocation sur le serveur n’a pas pu être confirmée.", "You are signed out of the launcher. Sign-out on the server could not be confirmed."));
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
        feedGeneration++;
        feedRequest?.Cancel();
        posts = [];
        feedMessage = null;
        NewsList.ItemsSource = null;
        ArticlePanel.Visibility = Visibility.Collapsed;
        SettingsOverlay.Visibility = Visibility.Collapsed;
        ArticleCaption.Text = ArticleTitle.Text = ArticleSummary.Text = ArticleBody.Text = "";
        SetAvailableText(() => T("Vérifiez les mises à jour après la connexion.", "Check for updates after signing in."));
        SetReleaseNotesText(() => T("Les changements de la version disponible apparaîtront ici.", "Changes in the available release will appear here."));
    }

    private async void Play_Click(object sender, RoutedEventArgs e)
    {
        if (!Authenticated || installed is not { } currentInstall || GameRunning || session is not { } currentSession) return;
        await RunOperationAsync(async token =>
        {
            SetStatus(() => T("Vérification de votre session…", "Verifying your session…"));
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
                    SetStatus(() => T("Le jeu est fermé. À bientôt sur Arkya.", "The game is closed. See you soon in Arkya."));
                    UpdateButtons();
                });
            };
            SetStatus(() => T("Démarrage d’Arkya…", "Launching Arkya…"));
            if (handoff is not null) await handoff.SendAsync(launched.Id, session, token);
            SetStatus(() => T("Arkya est en cours d’exécution.", "Arkya is running."));
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
                SetStatus(() => currentOperation.IsCancellationRequested ? T("Opération annulée.", "Operation cancelled.") : T("Le serveur ou le jeu n’a pas répondu à temps. Vous pouvez réessayer.", "The server or game did not respond in time. Please try again."));
        }
        catch (LauncherAuthenticationException exception)
        {
            if (exception.SessionInvalid)
            {
                EndSession();
                await DiscardSavedSessionAsync();
            }
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

    private async Task DiscardSavedSessionAsync()
    {
        try { await identity.ForgetSessionAsync(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException)
        {
            // A revoked credential cannot reopen the authenticated UI, even if its file is locked.
        }
    }

    private void UpdateButtons()
    {
        if (session is not null && !Authenticated && !refreshingSession)
        {
            EndSession();
            SetStatus(() => T("Votre session a expiré. Reconnectez-vous avec Discord pour continuer.", "Your session has expired. Sign in with Discord again to continue."));
        }
        var busy = operation is not null || refreshingSession;
        var connected = Authenticated;
        LoginPage.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
        LauncherContent.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
        AccountLabel.Text = session?.DisplayName ?? T("Non connecté", "Not signed in");
        AccountLabel.ToolTip = session?.DisplayName;
        LoginButton.IsEnabled = !busy;
        LoginProgress.Visibility = !connected && busy ? Visibility.Visible : Visibility.Collapsed;
        LoginProgress.IsIndeterminate = !connected && busy;
        LoginCancelButton.Visibility = !connected && busy ? Visibility.Visible : Visibility.Collapsed;
        LogoutButton.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
        LogoutButton.IsEnabled = connected && !busy;
        InstalledLabel.Text = installed is null ? T("Aucune version installée", "No version installed") : "Arkya " + installed.Manifest.Version;
        InstallButton.Content = IsCurrent ? T("Jeu à jour", "Game up to date") : installed is null ? T("Installer le jeu", "Install game") : T("Mettre à jour", "Update");
        InstallButton.IsEnabled = connected && !busy && !GameRunning && available is not null && !IsCurrent;
        InstallButton.Visibility = IsCurrent ? Visibility.Collapsed : Visibility.Visible;
        PlayButton.Content = GameRunning ? T("Jeu en cours", "Game running") : T("Jouer", "Play");
        PlayButton.IsEnabled = connected && !busy && !GameRunning && installed is not null;
        PlayButton.Visibility = installed is null ? Visibility.Collapsed : Visibility.Visible;
        CheckButton.IsEnabled = connected && !busy;
        BetaChannel.IsEnabled = StableChannel.IsEnabled = connected && !busy && !GameRunning;
        ChangeLocationButton.IsEnabled = SettingsLocationButton.IsEnabled = connected && !busy && !GameRunning;
        CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        LauncherUpdateButton.IsEnabled = !busy && !GameRunning && launcherUpdate is not null;
    }

    private static bool IsExpected(Exception exception) => exception is IOException or UnauthorizedAccessException
        or HttpRequestException or JsonException or Win32Exception or InvalidDataException or ArgumentException or LauncherAuthenticationException or CryptographicException;

    private void Cancel_Click(object sender, RoutedEventArgs e) => operation?.Cancel();

    private void ChangeLocation_Click(object sender, RoutedEventArgs e) => ChooseGameDirectory();

    private bool ChooseGameDirectory()
    {
        if (!Authenticated || operation is not null || GameRunning) return false;
        try
        {
            var dialog = new OpenFolderDialog
            {
                Title = T("Choisir le dossier d’installation du jeu Arkya", "Choose the Arkya game installation folder"),
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
            SetStatus(() => installed is null
                ? T("Dossier choisi. Cliquez sur Installer le jeu pour télécharger Arkya ici. Les autres installations sont conservées.",
                    "Folder selected. Click Install game to download Arkya here. Other installations are kept.")
                : T("Installation existante retrouvée dans le dossier choisi.", "An existing installation was found in the selected folder."));
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
        using var request = CancellationTokenSource.CreateLinkedTokenSource(requestedLifetime.Token);
        feedRequest = request;
        var token = request.Token;
        var requestedGeneration = feedGeneration;
        var requestedLanguage = CurrentLanguage;
        loadingFeed = true;
        feedFailed = false;
        var requestedKind = selectedKind;
        feedMessage = () => T("Chargement des publications…", "Loading publications…");
        RenderFeed();
        try
        {
            var address = "https://api.arkya.online/v1/content/posts?language=" + requestedLanguage + "&limit=12" +
                (requestedKind == "all" ? "" : "&kind=" + requestedKind);
            var response = await publications.GetPostsAsync(address, token);
            token.ThrowIfCancellationRequested();
            if (requestedGeneration == feedGeneration && ReferenceEquals(requestedLifetime, sessionLifetime))
            {
                posts = response.Where(post => post.Language == requestedLanguage).Select(post => new PostCard(post, requestedLanguage)).ToArray();
                feedMessage = () => T("Aucune publication dans cette rubrique pour le moment. Retrouvez bientôt les nouvelles de l’équipe Arkya.",
                    "No publications in this section yet. Check back soon for news from the Arkya team.");
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (windowLifetime.IsCancellationRequested) { return; }
        catch (Exception exception) when (IsExpected(exception) || exception is OperationCanceledException)
        {
            if (requestedGeneration == feedGeneration && ReferenceEquals(requestedLifetime, sessionLifetime))
            {
                feedFailed = true;
                feedMessage = () => T("Les publications sont momentanément indisponibles. Vous pouvez continuer à installer et lancer le jeu.",
                    "Publications are temporarily unavailable. You can still install and launch the game.");
            }
        }
        finally { loadingFeed = false; feedRequest = null; }
        if (!Authenticated || windowLifetime.IsCancellationRequested) return;
        if (!ReferenceEquals(requestedLifetime, sessionLifetime) || requestedGeneration != feedGeneration || requestedKind != selectedKind)
            await RefreshFeedAsync();
        else RenderFeed();
    }

    private void RenderFeed()
    {
        if (NewsList is null) return;
        var visible = posts.Where(post => selectedKind == "all" || post.Post.Kind == selectedKind).ToArray();
        NewsList.ItemsSource = visible;
        FeedHeading.Text = selectedKind switch { "news" => T("Actualités", "News"), "release-note" => T("Notes de version", "Release notes"), _ => T("À la une", "Featured") };
        FeedStatus.Text = feedMessage?.Invoke() ?? T("Les nouvelles de l’équipe Arkya apparaîtront ici.", "News from the Arkya team will appear here.");
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
        if (refreshingSession)
        {
            e.Cancel = true;
            closeAfterCancellation = true;
            return;
        }
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

    private sealed record PostCard(ContentPost Post, string Language)
    {
        public string Title => Post.Title;
        public string Summary => Post.Summary;
        public string Caption => (Post.Kind == "release-note" ? (Language == "en" ? "UPDATE " : "MISE À JOUR ") + Post.GameVersion : Language == "en" ? "NEWS" : "ACTUALITÉ") +
            "  ·  " + Post.PublishedAt.ToLocalTime().ToString("dd MMM yyyy", CultureInfo.GetCultureInfo(Language == "en" ? "en-GB" : "fr-FR"));
    }
}
