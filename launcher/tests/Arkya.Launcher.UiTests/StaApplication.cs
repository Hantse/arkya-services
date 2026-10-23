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
                // Load production resources without App.Run, OnStartup, a mutex or a launcher process.
                var application = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                application.InitializeComponent();
                ready.SetResult(Dispatcher.CurrentDispatcher);
                Dispatcher.Run();
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
        dispatcher.Invoke(() => Application.Current.Shutdown());
        dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
        if (!thread.Join(TimeSpan.FromSeconds(10))) throw new TimeoutException("The WPF test dispatcher did not stop.");
    }
}
