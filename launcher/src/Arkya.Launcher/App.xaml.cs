using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Arkya.Launcher;

public partial class App : Application
{
    private Mutex? instanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        instanceMutex = new Mutex(true, "Local\\Arkya.Launcher", out var firstInstance);
        if (!firstInstance)
        {
            MessageBox.Show("Le launcher Arkya est déjà ouvert.", "Arkya");
            Shutdown();
            return;
        }
        var window = new MainWindow();
        MainWindow = window;
        if (e.Args.Length is 2 or 4 && e.Args[0] == "--smoke-test")
        {
            if (e.Args.Length == 4)
            {
                window.Width = Math.Clamp(int.Parse(e.Args[2]), window.MinWidth, 1920);
                window.Height = Math.Clamp(int.Parse(e.Args[3]), window.MinHeight, 1200);
            }
            window.ShowInTaskbar = false;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -20000;
            window.Top = -20000;
            window.Show();
            window.UpdateLayout();
            var content = (FrameworkElement)window.Content;
            var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(content);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(Path.GetFullPath(e.Args[1]))) encoder.Save(stream);
            Shutdown();
            return;
        }
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
