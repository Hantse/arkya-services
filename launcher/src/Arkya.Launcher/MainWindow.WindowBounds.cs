using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Arkya.Launcher;

public partial class MainWindow
{
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(ConstrainMaximizedBounds);
    }

    private static nint ConstrainMaximizedBounds(nint window, int message, nint wParam, nint lParam, ref bool handled)
    {
        const int getMinMaxInfo = 0x0024;
        const uint nearestMonitor = 2;
        if (message != getMinMaxInfo) return 0;

        var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(window, nearestMonitor), ref monitor)) return 0;

        // Borderless windows otherwise maximize behind the taskbar. Both rectangles use device pixels.
        var limits = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        limits.MaxPosition.X = monitor.WorkArea.Left - monitor.MonitorArea.Left;
        limits.MaxPosition.Y = monitor.WorkArea.Top - monitor.MonitorArea.Top;
        limits.MaxSize.X = monitor.WorkArea.Right - monitor.WorkArea.Left;
        limits.MaxSize.Y = monitor.WorkArea.Bottom - monitor.WorkArea.Top;
        Marshal.StructureToPtr(limits, lParam, false);
        // Let WPF continue applying MinWidth/MinHeight and tracking limits.
        return 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize;
    }

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
}
