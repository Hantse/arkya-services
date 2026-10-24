using System.Windows;
using System.Windows.Threading;

namespace Arkya.Launcher.UiTests;

[CollectionDefinition("WPF", DisableParallelization = true)]
public sealed class WpfCollection : ICollectionFixture<StaApplication>;

public sealed class StaApplication : IDisposable
{
    private readonly Thread thread;
    private readonly Dispatcher dispatcher;

    public StaApplication()
    {
        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        thread = new Thread(() =>
        {
            try
            {
                // Application queues OnStartup in its constructor, even without Application.Run.
                // Suppress that callback so tests load resources without the launcher's mutex or services.
                var currentDispatcher = Dispatcher.CurrentDispatcher;
                DispatcherOperation? startup = null;
                void CaptureStartup(object? sender, DispatcherHookEventArgs e)
                {
                    if (e.Operation.Priority == DispatcherPriority.Send) startup ??= e.Operation;
                }
                currentDispatcher.Hooks.OperationPosted += CaptureStartup;
                App application;
                try { application = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown }; }
                finally { currentDispatcher.Hooks.OperationPosted -= CaptureStartup; }
                if (startup is null || !startup.Abort()) throw new InvalidOperationException("Could not suppress production launcher startup.");
                application.InitializeComponent();
                ready.SetResult(currentDispatcher);
                application.Run();
            }
            catch (Exception exception) { ready.TrySetException(exception); }
        }) { IsBackground = true, Name = "Arkya launcher UI tests" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        dispatcher = ready.Task.WaitAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
    }

    public Task RunAsync(Action action) => dispatcher.InvokeAsync(action).Task;
    public Task RunAsync(Func<Task> action) => dispatcher.InvokeAsync(action).Task.Unwrap();

    public void Dispose()
    {
        // xUnit may dispose the fixture on the dispatcher after an asynchronous UI test.
        if (dispatcher.CheckAccess())
        {
            Application.Current.Shutdown();
            return;
        }
        dispatcher.Invoke(() => Application.Current.Shutdown());
        if (!thread.Join(TimeSpan.FromSeconds(10))) throw new TimeoutException("The WPF test dispatcher did not stop.");
    }
}
