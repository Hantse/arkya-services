using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Arkya.Launcher.Core;
using Xunit.Abstractions;

namespace Arkya.Launcher.UiTests;

[Collection("WPF")]
public sealed class AuthenticationGateTests(StaApplication application, ITestOutputHelper output)
{
    [Fact]
    public Task LoadedWindowShowsOnlyTheDiscordLoginPage() => application.RunAsync(() =>
    {
        using var fixture = new WindowFixture();
        var window = fixture.Window;
        Assert.True(window.IsLoaded);
        Assert.Equal(Visibility.Visible, Control<Grid>(window, "LoginPage").Visibility);
        Assert.Equal(Visibility.Collapsed, Control<Grid>(window, "LauncherContent").Visibility);
        Assert.True(Control<Button>(window, "LoginButton").IsEnabled);
        Assert.Equal("Continuer avec Discord", Control<Button>(window, "LoginButton").Content);
        Assert.Equal(Visibility.Collapsed, Control<ProgressBar>(window, "LoginProgress").Visibility);
        Assert.Equal(Visibility.Collapsed, Control<Button>(window, "LoginCancelButton").Visibility);
        AssertProtectedCommandsDisabled(window);
        Assert.Equal(0, fixture.Network.Requests);
        Assert.False(Directory.Exists(Path.Combine(fixture.DataDirectory, "Launcher")));
    });

    [Fact]
    public Task ValidSessionRevealsTheLauncherAndAccount() => application.RunAsync(() =>
    {
        using var fixture = new WindowFixture();
        Authenticate(fixture.Window);
        var window = fixture.Window;
        Assert.Equal(Visibility.Collapsed, Control<Grid>(window, "LoginPage").Visibility);
        Assert.Equal(Visibility.Visible, Control<Grid>(window, "LauncherContent").Visibility);
        Assert.Equal("Aélyra", Control<TextBlock>(window, "AccountLabel").Text);
        Assert.True(Control<Button>(window, "CheckButton").IsEnabled);
        Assert.True(Control<Button>(window, "ChangeLocationButton").IsEnabled);
        Assert.True(Control<RadioButton>(window, "BetaChannel").IsEnabled);
        Assert.True(Control<RadioButton>(window, "StableChannel").IsEnabled);
        Assert.Equal(Visibility.Visible, Control<Button>(window, "LogoutButton").Visibility);
        Assert.Equal(0, fixture.Network.Requests);
    });

    [Fact]
    public Task ExpiryCancelsInFlightWorkAndClearsTheAuthenticatedUi() => application.RunAsync(async () =>
    {
        using var fixture = new WindowFixture();
        var window = fixture.Window;
        Authenticate(window);
        Field(window, "available", Manifest());
        Control<ItemsControl>(window, "NewsList").ItemsSource = new[] { "Private cached article" };
        Control<Border>(window, "ArticlePanel").Visibility = Visibility.Visible;
        Control<Border>(window, "SettingsOverlay").Visibility = Visibility.Visible;
        Control<TextBlock>(window, "ArticleBody").Text = "Cached article body";
        CancellationToken observedToken = default;
        var running = (Task)Invoke(window, "RunOperationAsync", new Func<CancellationToken, Task>(token =>
        {
            observedToken = token;
            return Task.Delay(Timeout.InfiniteTimeSpan, token);
        }), true)!;
        Assert.False(running.IsCompleted);
        Assert.False(observedToken.IsCancellationRequested);
        Field(window, "session", Session(DateTimeOffset.UtcNow.AddSeconds(-1)));
        Invoke(window, "UpdateButtons");
        Assert.True(observedToken.IsCancellationRequested);
        await running;
        Assert.Null(Field(window, "session"));
        Assert.Null(Field(window, "sessionLifetime"));
        Assert.Null(Field(window, "operation"));
        Assert.Null(Field(window, "available"));
        Assert.Null(Control<ItemsControl>(window, "NewsList").ItemsSource);
        Assert.Equal("", Control<TextBlock>(window, "ArticleBody").Text);
        Assert.Equal(Visibility.Collapsed, Control<Border>(window, "ArticlePanel").Visibility);
        Assert.Equal(Visibility.Collapsed, Control<Border>(window, "SettingsOverlay").Visibility);
        Assert.Equal(Visibility.Visible, Control<Grid>(window, "LoginPage").Visibility);
        Assert.Equal(Visibility.Collapsed, Control<Grid>(window, "LauncherContent").Visibility);
        Assert.Contains("expiré", Control<TextBlock>(window, "LoginStatus").Text);
        AssertProtectedCommandsDisabled(window);
        Assert.Equal(0, fixture.Network.Requests);
    });

    [Fact]
    public Task LoggedOutHandlersCannotFetchInstallLaunchOrChangeSettings() => application.RunAsync(async () =>
    {
        using var fixture = new WindowFixture();
        var window = fixture.Window;
        var selected = (GameInstallationSelection)Field(window, "selection")!;
        var preferences = selected.Preferences;
        Field(window, "available", Manifest());
        Field(window, "installed", new InstalledGame(Manifest(), "releases/never-launched", "beta"));
        await (Task)Invoke(window, "CheckAsync")!;
        await (Task)Invoke(window, "RefreshFeedAsync")!;
        Invoke(window, "Check_Click", window, new RoutedEventArgs());
        Invoke(window, "RefreshFeed_Click", window, new RoutedEventArgs());
        Invoke(window, "Install_Click", window, new RoutedEventArgs());
        Invoke(window, "Play_Click", window, new RoutedEventArgs());
        Invoke(window, "ChangeLocation_Click", window, new RoutedEventArgs());
        Assert.False((bool)Invoke(window, "ChooseGameDirectory")!);
        Invoke(window, "Settings_Click", window, new RoutedEventArgs());
        Invoke(window, "Channel_Checked", Control<RadioButton>(window, "StableChannel"), new RoutedEventArgs());
        Assert.Equal(preferences, selected.Preferences);
        Assert.False(Directory.Exists(Path.Combine(fixture.DataDirectory, "Launcher")));
        Assert.Equal(Visibility.Collapsed, Control<Border>(window, "SettingsOverlay").Visibility);
        Assert.Null(Field(window, "operation"));
        Assert.Equal(0, fixture.Network.Requests);
    });

    [Fact]
    public Task DistributionUiOffersOnlyBetaAndStableWithNoEditableAddress() => application.RunAsync(() =>
    {
        using var fixture = new WindowFixture();
        Authenticate(fixture.Window);
        Invoke(fixture.Window, "Settings_Click", fixture.Window, new RoutedEventArgs());
        var channels = Descendants(fixture.Window).OfType<RadioButton>().Where(button => button.GroupName == "Channel").ToArray();
        Assert.Equal(new[] { "beta", "stable" }, channels.Select(button => button.Tag as string).OrderBy(value => value));
        Assert.Equal(new[] { "Bêta", "Stable" }, channels.Select(button => button.Content as string).OrderBy(value => value));
        Assert.Empty(Descendants(fixture.Window).OfType<TextBox>());
        Assert.Null(fixture.Window.FindName("ManifestUrl"));
        Assert.Null(fixture.Window.FindName("SettingsPanel"));
        Assert.Equal(0, fixture.Network.Requests);
    });

    [Fact]
    public Task SignedOutPlayerCanChangeLanguageAndBothSelectorsStayInSync() => application.RunAsync(async () =>
    {
        using var fixture = new WindowFixture();
        var window = fixture.Window;
        Invoke(window, "ShowError", new HttpRequestException("Synthetic network failure"));
        Assert.Contains("Connexion au serveur impossible", Control<TextBlock>(window, "LoginStatus").Text);

        await SelectLanguageAsync(window, "LoginLanguageSelector", "en");

        Assert.Equal("en", Control<ComboBox>(window, "LoginLanguageSelector").SelectedValue);
        Assert.Equal("en", Control<ComboBox>(window, "LauncherLanguageSelector").SelectedValue);
        Assert.Equal("Continue with Discord", Control<Button>(window, "LoginButton").Content);
        Assert.Equal("The game", Control<RadioButton>(window, "HomeTab").Content);
        Assert.Equal("Not signed in", Control<TextBlock>(window, "AccountLabel").Text);
        Assert.Equal("Featured", Control<TextBlock>(window, "FeedHeading").Text);
        Assert.Equal("Unable to connect to the server. Check your connection and try again.", Control<TextBlock>(window, "LoginStatus").Text);
        Assert.Equal(Control<TextBlock>(window, "LoginStatus").Text, Control<TextBlock>(window, "StatusLabel").Text);
        Assert.Equal(Visibility.Visible, Control<Grid>(window, "LoginPage").Visibility);
        AssertProtectedCommandsDisabled(window);

        await SelectLanguageAsync(window, "LoginLanguageSelector", "fr");
        Assert.Equal("fr", Control<ComboBox>(window, "LauncherLanguageSelector").SelectedValue);
        Assert.Equal("Continuer avec Discord", Control<Button>(window, "LoginButton").Content);
        Assert.Contains("Connexion au serveur impossible", Control<TextBlock>(window, "LoginStatus").Text);
        Assert.Equal(0, fixture.Network.Requests);
    });

    [Fact]
    public Task SelectedLanguageIsRestoredWhenTheLauncherWindowIsRecreated() => application.RunAsync(async () =>
    {
        using var fixture = new WindowFixture();
        await SelectLanguageAsync(fixture.Window, "LoginLanguageSelector", "en");
        fixture.Window.Close();

        using var reopened = new WindowFixture(dataDirectory: fixture.DataDirectory);
        Assert.Equal("en", Control<ComboBox>(reopened.Window, "LoginLanguageSelector").SelectedValue);
        Assert.Equal("en", Control<ComboBox>(reopened.Window, "LauncherLanguageSelector").SelectedValue);
        Assert.Equal("Continue with Discord", Control<Button>(reopened.Window, "LoginButton").Content);
        Assert.Equal("en", ((GameInstallationSelection)Field(reopened.Window, "selection")!).Preferences.Language);
        Assert.Equal(Visibility.Visible, Control<Grid>(reopened.Window, "LoginPage").Visibility);
        Assert.Equal(0, fixture.Network.Requests + reopened.Network.Requests);
    });

    [Fact]
    public Task ConnectedLanguageSwitchPreservesSessionChannelAndGameLocation() => application.RunAsync(async () =>
    {
        using var fixture = new WindowFixture();
        var window = fixture.Window;
        using var feedHandler = new LocalizedFeedHandler();
        using var feedClient = new HttpClient(feedHandler);
        Field(window, "publications", new ContentFeedClient(feedClient));
        var selection = (GameInstallationSelection)Field(window, "selection")!;
        selection.SelectDirectory(Path.Combine(fixture.DataDirectory, "chosen game"));
        selection.SelectChannel(ReleaseChannel.Stable);
        Invoke(window, "UpdateChannel");
        Invoke(window, "UpdateLocation");
        Authenticate(window);
        var session = Field(window, "session");
        var lifetime = Field(window, "sessionLifetime");
        var before = selection.Preferences;
        var installer = selection.Installer;
        var location = Control<TextBlock>(window, "FooterLocationLabel").Text;
        var sessionPath = Path.Combine(fixture.DataDirectory, "Launcher", "launcher-session.dat");
        byte[] protectedSession = [1, 128, 255, 17];
        File.WriteAllBytes(sessionPath, protectedSession);

        await SelectLanguageAsync(window, "LauncherLanguageSelector", "en");

        Assert.Equal(before with { Language = "en" }, selection.Preferences);
        Assert.Same(installer, selection.Installer);
        Assert.Same(session, Field(window, "session"));
        Assert.Same(lifetime, Field(window, "sessionLifetime"));
        Assert.Equal(protectedSession, File.ReadAllBytes(sessionPath));
        Assert.Equal(location, Control<TextBlock>(window, "FooterLocationLabel").Text);
        Assert.True(Control<RadioButton>(window, "StableChannel").IsChecked);
        Assert.Equal("Aélyra", Control<TextBlock>(window, "AccountLabel").Text);
        Assert.Equal("Sign out", Control<Button>(window, "LogoutButton").Content);
        Assert.Equal("Install game", Control<Button>(window, "InstallButton").Content);
        Assert.Equal("No version installed", Control<TextBlock>(window, "InstalledLabel").Text);
        Assert.Equal(Visibility.Visible, Control<Grid>(window, "LauncherContent").Visibility);
        Assert.Equal(0, fixture.Network.Requests);
        Assert.Single(feedHandler.Requests);
        Assert.Contains("language=en", feedHandler.Requests[0].Query);
    });

    [Theory]
    [InlineData("fr", "Nouvelles françaises", "Actualités")]
    [InlineData("en", "English news", "News")]
    public Task PublicationRequestAndDisplayedCardsFollowTheSelectedLanguage(string language, string expectedTitle, string expectedHeading) => application.RunAsync(async () =>
    {
        using var fixture = new WindowFixture();
        var window = fixture.Window;
        await SelectLanguageAsync(window, "LoginLanguageSelector", language);
        using var handler = new LocalizedFeedHandler();
        using var client = new HttpClient(handler);
        Field(window, "publications", new ContentFeedClient(client));
        Authenticate(window);
        var newsTab = Assert.Single(Descendants(window).OfType<RadioButton>(), button => (string?)button.Tag == "news");
        newsTab.IsChecked = true;
        await RenderAsync(window);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("api.arkya.online", request.Host);
        Assert.Contains("language=" + language, request.Query);
        Assert.Contains("kind=news", request.Query);
        Assert.Equal(expectedHeading, Control<TextBlock>(window, "FeedHeading").Text);
        Assert.Equal(new[] { expectedTitle }, CardTitles(window));
        Assert.Equal(0, fixture.Network.Requests);
    });

    [Fact]
    public Task LanguageSwitchIgnoresThePreviousLanguageResponseAndRefreshesTheNewFeed() => application.RunAsync(async () =>
    {
        using var fixture = new WindowFixture();
        var window = fixture.Window;
        using var handler = new LocalizedFeedHandler(delayFrench: true);
        using var client = new HttpClient(handler);
        Field(window, "publications", new ContentFeedClient(client));
        Authenticate(window);
        var frenchRequest = (Task)Invoke(window, "RefreshFeedAsync")!;
        Assert.False(frenchRequest.IsCompleted);
        Assert.Single(handler.Requests);
        Control<Border>(window, "ArticlePanel").Visibility = Visibility.Visible;
        Control<TextBlock>(window, "ArticleBody").Text = "Ancien article français";

        await SelectLanguageAsync(window, "LauncherLanguageSelector", "en");
        Assert.Empty(CardTitles(window));
        Assert.Equal(Visibility.Collapsed, Control<Border>(window, "ArticlePanel").Visibility);
        Assert.Equal("", Control<TextBlock>(window, "ArticleBody").Text);
        handler.CompleteFrenchResponse();
        await frenchRequest.WaitAsync(TimeSpan.FromSeconds(5));
        await RenderAsync(window);

        Assert.Equal(new[] { "English news" }, CardTitles(window));
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("language=fr", handler.Requests[0].Query);
        Assert.Contains("language=en", handler.Requests[1].Query);
        Assert.Equal("Featured", Control<TextBlock>(window, "FeedHeading").Text);
        Assert.DoesNotContain("indisponibles", Control<TextBlock>(window, "FeedStatus").Text);
        Assert.Equal(0, fixture.Network.Requests);
    });

    [Theory]
    [InlineData("fr", false)]
    [InlineData("en", false)]
    [InlineData("fr", true)]
    [InlineData("en", true)]
    public Task LocalizedLoginAndLauncherRenderAtMinimumWindowSize(string language, bool connected) => application.RunAsync(async () =>
    {
        using var fixture = new WindowFixture(1040, 720);
        var window = fixture.Window;
        await SelectLanguageAsync(window, "LoginLanguageSelector", language);
        if (connected)
        {
            Field(window, "available", Manifest());
            Field(window, "installed", new InstalledGame(Manifest() with { Sha256 = new string('b', 64) }, "releases/ui-fixture", "beta"));
            Authenticate(window);
            Invoke(window, "ShowError", new HttpRequestException("Synthetic network failure"));
        }
        await RenderAsync(window);
        var content = (FrameworkElement)window.Content;
        var selector = Control<ComboBox>(window, connected ? "LauncherLanguageSelector" : "LoginLanguageSelector");
        AssertVisibleInside(selector, content);
        Assert.Equal(language == "en" ? "English" : "Français", Assert.IsType<ComboBoxItem>(selector.SelectedItem).Content);
        foreach (var name in connected
            ? new[] { "HomeTab", "BetaChannel", "StableChannel", "ChangeLocationButton", "InstallButton", "PlayButton", "CheckButton", "LogoutButton", "StatusLabel" }
            : new[] { "LoginButton", "LoginStatus" })
            AssertVisibleInside(Control<FrameworkElement>(window, name), content);
        if (connected)
            foreach (var tab in Descendants(window).OfType<RadioButton>().Where(button => button.GroupName == "Feed"))
                AssertVisibleInside(tab, content);
        SaveScreenshot(content, $"localized-{(connected ? "authenticated" : "login")}-{language}-1040x720.png");
        Assert.Equal(0, fixture.Network.Requests);
    });

    private static async Task SelectLanguageAsync(MainWindow window, string selectorName, string language)
    {
        Control<ComboBox>(window, selectorName).SelectedValue = language;
        await RenderAsync(window);
    }

    private static async Task RenderAsync(MainWindow window)
    {
        window.UpdateLayout();
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
    }

    private static string[] CardTitles(MainWindow window) => Control<ItemsControl>(window, "NewsList").Items.Cast<object>()
        .Select(card => (string)card.GetType().GetProperty("Title")!.GetValue(card)!).ToArray();

    private sealed class LocalizedFeedHandler(bool delayFrench = false) : HttpMessageHandler
    {
        private readonly TaskCompletionSource<HttpResponseMessage> frenchResponse = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            // Deliberately ignore cancellation to exercise an old response completing after a language switch.
            if (delayFrench && request.RequestUri!.Query.Contains("language=fr", StringComparison.Ordinal)) return frenchResponse.Task;
            return Task.FromResult(Response());
        }

        public void CompleteFrenchResponse() => frenchResponse.SetResult(Response());

        protected override void Dispose(bool disposing)
        {
            if (disposing) frenchResponse.TrySetCanceled();
            base.Dispose(disposing);
        }

        private static HttpResponseMessage Response() => new(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                items = new[]
                {
                    new ContentPost("french-post", "news", "fr", "Nouvelles françaises", "Résumé", "Article français", null, DateTimeOffset.UtcNow),
                    new ContentPost("english-post", "news", "en", "English news", "Summary", "English article", null, DateTimeOffset.UtcNow)
                },
                hasMore = false
            }, GameManifest.JsonOptions))
        };
    }

    [Theory]
    [InlineData(1280, 830)]
    [InlineData(1040, 720)]
    public Task LoginPageRendersWithVisibleDiscordActionAndInstructions(int width, int height) => application.RunAsync(async () =>
    {
        using var fixture = new WindowFixture(width, height);
        var window = fixture.Window;
        window.UpdateLayout();
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        var content = (FrameworkElement)window.Content;
        var title = Assert.Single(Descendants(Control<Grid>(window, "LoginPage")).OfType<TextBlock>(),
            block => block.Text == "Votre aventure\nvous attend.");
        foreach (var element in new FrameworkElement[] { Control<Button>(window, "LoginButton"), Control<TextBlock>(window, "LoginStatus"), title })
            AssertVisibleInside(element, content);
        Assert.Equal(Visibility.Collapsed, Control<Grid>(window, "LauncherContent").Visibility);
        SaveScreenshot(content, $"login-{width}x{height}.png");
        Assert.Equal(0, fixture.Network.Requests);
    });

    [Theory]
    [InlineData(1280, 830)]
    [InlineData(1040, 720)]
    public Task AuthenticatedWindowRendersWithAccessibleFooterActions(int width, int height) => application.RunAsync(async () =>
    {
        using var fixture = new WindowFixture(width, height);
        var window = fixture.Window;
        Authenticate(window);
        Field(window, "available", Manifest());
        Invoke(window, "UpdateButtons");
        window.UpdateLayout();
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        var content = (FrameworkElement)window.Content;
        foreach (var name in new[] { "BetaChannel", "StableChannel", "ChangeLocationButton", "InstallButton", "CheckButton" })
            AssertVisibleInside(Control<FrameworkElement>(window, name), content);
        SaveScreenshot(content, $"authenticated-{width}x{height}.png");
        Assert.Equal(0, fixture.Network.Requests);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task MaximizedWindowKeepsFooterInsideTheMonitorWorkArea(bool updateAvailable) => application.RunAsync(async () =>
    {
        using var fixture = new WindowFixture();
        var window = fixture.Window;
        Authenticate(window);
        Field(window, "available", Manifest());
        Field(window, "installed", new InstalledGame(Manifest() with { Sha256 = new string('b', 64) }, "releases/ui-fixture", "beta"));
        Invoke(window, "UpdateButtons");
        if (updateAvailable)
        {
            Control<Border>(window, "LauncherUpdateBanner").Visibility = Visibility.Visible;
            Control<TextBlock>(window, "LauncherUpdateStatus").Text = "Une mise à jour du launcher est disponible. Votre connexion et vos jeux seront conservés.";
        }
        // Keep this native maximization test invisible and leave the user's active window alone.
        window.Opacity = 0;
        window.Left = SystemParameters.WorkArea.Left + 16;
        window.Top = SystemParameters.WorkArea.Top + 16;
        window.UpdateLayout();
        var restoreBounds = window.RestoreBounds;
        Invoke(window, "Maximize_Click", window, new RoutedEventArgs());
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
        Assert.Equal(WindowState.Maximized, window.WindowState);
        var handle = new WindowInteropHelper(window).Handle;
        var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        Assert.True(GetMonitorInfo(MonitorFromWindow(handle, 2), ref monitor));
        var content = (FrameworkElement)window.Content;
        var actions = new[] { "BetaChannel", "StableChannel", "ChangeLocationButton", "InstallButton", "PlayButton", "CheckButton" };
        foreach (var name in updateAvailable ? actions.Append("LauncherUpdateButton") : actions)
            AssertInsideWorkArea(Control<FrameworkElement>(window, name), content, monitor.WorkArea);
        AssertInsideWorkArea(content, content, monitor.WorkArea);
        SaveScreenshot(content, $"authenticated-maximized{(updateAvailable ? "-update" : "")}.png");

        Invoke(window, "Maximize_Click", window, new RoutedEventArgs());
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
        Assert.Equal(WindowState.Normal, window.WindowState);
        Assert.InRange(Math.Abs(window.ActualWidth - restoreBounds.Width), 0, 1);
        Assert.InRange(Math.Abs(window.ActualHeight - restoreBounds.Height), 0, 1);
        foreach (var name in actions) AssertVisibleInside(Control<FrameworkElement>(window, name), content);
        Assert.Equal(0, fixture.Network.Requests);
    });

    private static void AssertInsideWorkArea(FrameworkElement element, FrameworkElement content, NativeRect workArea)
    {
        AssertVisibleInside(element, content);
        var topLeft = element.PointToScreen(new Point());
        var bottomRight = element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight));
        Assert.True(topLeft.X >= workArea.Left - 1 && topLeft.Y >= workArea.Top - 1 &&
            bottomRight.X <= workArea.Right + 1 && bottomRight.Y <= workArea.Bottom + 1,
            $"{element.Name} screen bounds {topLeft}–{bottomRight} must fit work area {workArea.Left},{workArea.Top}–{workArea.Right},{workArea.Bottom}.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect MonitorArea, WorkArea;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint window, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [Fact]
    public Task ProtectedSessionRestoresAfterTheWindowIsRecreated() => application.RunAsync(async () =>
    {
        using var fixture = new WindowFixture();
        var directory = Path.Combine(fixture.DataDirectory, "Launcher");
        var store = new ProtectedLauncherSessionStore(directory);
        var expiry = DateTimeOffset.UtcNow.AddDays(30);
        store.Save(new LauncherRefreshCredential(new string('a', 32) + "." + new string('A', 43),
            "11111111-1111-1111-1111-111111111111", expiry));
        using var handler = new NativeSessionHandler(expiry);
        using var client = new HttpClient(handler);
        Field(fixture.Window, "identity", new LauncherIdentityClient(client, sessionStore: store));
        await (Task)Invoke(fixture.Window, "RestoreConnectionAsync")!;
        Assert.Equal(Visibility.Visible, Control<Grid>(fixture.Window, "LauncherContent").Visibility);
        fixture.Window.Close();
        Assert.NotNull(store.Load());

        var reopened = (MainWindow)Activator.CreateInstance(typeof(MainWindow), BindingFlags.Instance | BindingFlags.NonPublic,
            null, new object[] { fixture.DataDirectory }, null)!;
        try
        {
            Field(reopened, "identity", new LauncherIdentityClient(client, sessionStore: new ProtectedLauncherSessionStore(directory)));
            using var deniedClient = new HttpClient(new DenyNetworkHandler());
            Field(reopened, "releases", new ReleaseClient(deniedClient));
            Field(reopened, "publications", new ContentFeedClient(deniedClient));
            await (Task)Invoke(reopened, "RestoreConnectionAsync")!;
            Assert.Equal("Aélyra", Control<TextBlock>(reopened, "AccountLabel").Text);
            Assert.Equal(Visibility.Collapsed, Control<Grid>(reopened, "LoginPage").Visibility);
            Assert.Equal(2, handler.Rotations);
        }
        finally { reopened.Close(); }
    });

    [Fact]
    public Task ExplicitLogoutClearsTheProtectedSessionAndRevokesItsFamily() => application.RunAsync(async () =>
    {
        using var fixture = new WindowFixture();
        var store = new ProtectedLauncherSessionStore(Path.Combine(fixture.DataDirectory, "Launcher"));
        var expiry = DateTimeOffset.UtcNow.AddDays(30);
        store.Save(new LauncherRefreshCredential(new string('a', 32) + "." + new string('A', 43),
            "11111111-1111-1111-1111-111111111111", expiry));
        using var handler = new NativeSessionHandler(expiry);
        using var client = new HttpClient(handler);
        Field(fixture.Window, "identity", new LauncherIdentityClient(client, sessionStore: store));
        await (Task)Invoke(fixture.Window, "RestoreConnectionAsync")!;
        Invoke(fixture.Window, "Logout_Click", fixture.Window, new RoutedEventArgs());
        await fixture.Window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
        Assert.Null(store.Load());
        Assert.Equal(1, handler.Logouts);
        Assert.Equal(Visibility.Visible, Control<Grid>(fixture.Window, "LoginPage").Visibility);
    });

    [Fact]
    public Task LauncherUpdateRemainsAvailableWithoutLoginAndWaitsForBusyWork() => application.RunAsync(async () =>
    {
        using var fixture = new WindowFixture();
        Field(fixture.Window, "launcherUpdate", new LauncherUpdateManifest(1, "0.5.0", "win-x64",
            "https://github.com/Hantse/arkya-services/releases/download/launcher-v0.5.0/Arkya-Launcher-Setup.exe", 1, new string('a', 64)));
        Control<Border>(fixture.Window, "LauncherUpdateBanner").Visibility = Visibility.Visible;
        Control<TextBlock>(fixture.Window, "LauncherUpdateStatus").Text = "Arkya Launcher 0.5.0 est disponible. Votre connexion sera conservée.";
        Invoke(fixture.Window, "UpdateButtons");
        fixture.Window.UpdateLayout();
        Assert.True(Control<Button>(fixture.Window, "LauncherUpdateButton").IsEnabled);
        AssertVisibleInside(Control<Button>(fixture.Window, "LauncherUpdateButton"), (FrameworkElement)fixture.Window.Content);
        await fixture.Window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        SaveScreenshot((FrameworkElement)fixture.Window.Content, "launcher-update-1040x720.png");
        using var operation = new CancellationTokenSource();
        Field(fixture.Window, "operation", operation);
        Invoke(fixture.Window, "UpdateButtons");
        Assert.False(Control<Button>(fixture.Window, "LauncherUpdateButton").IsEnabled);
        Assert.Equal(0, fixture.Network.Requests);
    });

    private sealed class NativeSessionHandler(DateTimeOffset expiry) : HttpMessageHandler
    {
        public int Rotations { get; private set; }
        public int Logouts { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/v1/auth/refresh/native")
            {
                Rotations++;
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { accountId = "11111111-1111-1111-1111-111111111111", displayName = "Aélyra",
                        sessionToken = new string((char)('A' + Rotations), 43), expiresAt = DateTimeOffset.UtcNow.AddHours(8),
                        refreshToken = new string('a', 32) + "." + new string((char)('A' + Rotations), 43), refreshExpiresAt = expiry })
                });
            }
            if (path == "/v1/auth/me") return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { accountId = "11111111-1111-1111-1111-111111111111", displayName = "Aélyra",
                    expiresAt = DateTimeOffset.UtcNow.AddHours(8), isBanned = false })
            });
            Assert.Equal("/v1/auth/logout/native", path);
            Logouts++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NoContent));
        }
    }

    private static void AssertVisibleInside(FrameworkElement element, FrameworkElement content)
    {
        Assert.True(element.IsVisible, element.Name + " should be visible.");
        Assert.True(element.ActualWidth > 0 && element.ActualHeight > 0, element.Name + " should have a layout.");
        var bounds = element.TransformToAncestor(content).TransformBounds(new Rect(element.RenderSize));
        Assert.InRange(bounds.Left, 0, content.ActualWidth);
        Assert.InRange(bounds.Top, 0, content.ActualHeight);
        Assert.InRange(bounds.Right, 0, content.ActualWidth + 1);
        Assert.InRange(bounds.Bottom, 0, content.ActualHeight + 1);
    }

    private void SaveScreenshot(FrameworkElement content, string fileName)
    {
        var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var directory = Environment.GetEnvironmentVariable("ARKYA_LAUNCHER_UI_ARTIFACTS") ?? Path.Combine(AppContext.BaseDirectory, "ui-artifacts");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(path)) encoder.Save(file);
        Assert.True(new FileInfo(path).Length > 10000);
        output.WriteLine(path);
    }

    private static void Authenticate(MainWindow window)
    {
        Field(window, "sessionLifetime", new CancellationTokenSource());
        Field(window, "session", Session(DateTimeOffset.UtcNow.AddHours(1)));
        Invoke(window, "UpdateButtons");
    }

    private static LauncherSession Session(DateTimeOffset expiresAt) => new(new string('A', 43), "11111111-1111-1111-1111-111111111111", "Aélyra", expiresAt);
    private static GameManifest Manifest() => new(1, "ui-fixture", "win-x64", "https://never-requested.example.test/game.zip", 1, 1, new string('a', 64), "Arkya.exe");

    private static void AssertProtectedCommandsDisabled(MainWindow window)
    {
        foreach (var name in new[] { "InstallButton", "PlayButton", "CheckButton", "ChangeLocationButton", "SettingsLocationButton", "LogoutButton" })
            Assert.False(Control<Button>(window, name).IsEnabled, name + " must require authentication.");
        Assert.False(Control<RadioButton>(window, "BetaChannel").IsEnabled);
        Assert.False(Control<RadioButton>(window, "StableChannel").IsEnabled);
    }

    private static T Control<T>(MainWindow window, string name) where T : FrameworkElement => Assert.IsAssignableFrom<T>(window.FindName(name));
    private static object? Field(MainWindow window, string name) => typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window);
    private static void Field(MainWindow window, string name, object? value) => typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, value);
    private static object? Invoke(MainWindow window, string name, params object?[] arguments) => typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, arguments);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var descendant in Descendants(child)) yield return descendant;
    }

    private sealed class WindowFixture : IDisposable
    {
        public string DataDirectory { get; }
        public MainWindow Window { get; }
        public DenyNetworkHandler Network { get; } = new();
        private readonly HttpClient networkClient;

        public WindowFixture(int width = 1280, int height = 830, string? dataDirectory = null)
        {
            DataDirectory = dataDirectory ?? Path.Combine(Path.GetTempPath(), "arkya-launcher-ui-" + Guid.NewGuid().ToString("N"));
            networkClient = new HttpClient(Network);
            Window = (MainWindow)Activator.CreateInstance(typeof(MainWindow), BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { DataDirectory }, null)!;
            // No test may access the internet, even if an authentication guard regresses.
            Field(Window, "releases", new ReleaseClient(networkClient));
            Field(Window, "publications", new ContentFeedClient(networkClient));
            Field(Window, "identity", new LauncherIdentityClient(networkClient));
            Window.ShowInTaskbar = false;
            Window.ShowActivated = false;
            Window.WindowStartupLocation = WindowStartupLocation.Manual;
            Window.Left = Window.Top = -20000;
            Window.Width = width;
            Window.Height = height;
            Window.Show();
            Window.UpdateLayout();
        }

        public void Dispose()
        {
            Field(Window, "operation", null);
            Window.Close();
            networkClient.Dispose();
            var resolved = Path.GetFullPath(DataDirectory);
            var temporary = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!resolved.StartsWith(temporary, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("arkya-launcher-ui-", StringComparison.Ordinal))
                throw new InvalidOperationException("Refusing to clean a directory outside this UI test.");
            if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
        }
    }

    public sealed class DenyNetworkHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            throw new HttpRequestException("Network access is forbidden in launcher UI tests.");
        }
    }
}
