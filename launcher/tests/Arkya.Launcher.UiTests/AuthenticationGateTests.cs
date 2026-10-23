using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
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

    private static LauncherSession Session(DateTimeOffset expiresAt) => new(new string('A', 43), "11111111111111111111111111111111", "Aélyra", expiresAt);
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
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "arkya-launcher-ui-" + Guid.NewGuid().ToString("N"));
        public MainWindow Window { get; }
        public DenyNetworkHandler Network { get; } = new();
        private readonly HttpClient networkClient;

        public WindowFixture(int width = 1280, int height = 830)
        {
            networkClient = new HttpClient(Network);
            Window = (MainWindow)Activator.CreateInstance(typeof(MainWindow), BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { DataDirectory }, null)!;
            // No test may access the internet, even if an authentication guard regresses.
            Field(Window, "releases", new ReleaseClient(networkClient));
            Field(Window, "publications", new ContentFeedClient(networkClient));
            Field(Window, "identity", new LauncherIdentityClient(networkClient));
            Window.ShowInTaskbar = false;
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
