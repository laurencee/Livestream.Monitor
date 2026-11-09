using System.Runtime.InteropServices;

namespace Livestream.Monitor.Direct2D.Native;

/// <summary>Monitor lookups in device pixels, matching the placement the WPF overlay saves.</summary>
internal static class Monitors
{
    private const int MinimumDeviceLength = 1;

    // The enumeration callback cannot capture state; lookups run on the UI thread only.
    private static string? wantedDeviceName;
    private static Win32.Rect? foundWorkArea;

    public static Win32.Rect GetWorkArea(Win32.Point point) => GetInfo(FindMonitor(point)).WorkArea;

    // Windows uses the pre-minimize bounds to find the monitor of a minimized window.
    public static Win32.Rect GetWindowWorkArea(nint window) => GetInfo(Win32.MonitorFromWindow(window, Win32.MonitorDefaultToNearest)).WorkArea;

    /// <summary>The primary monitor's height in logical units at the system DPI, as WPF's SystemParameters.PrimaryScreenHeight.</summary>
    public static float PrimaryScreenHeight =>
        Win32.GetSystemMetrics(Win32.SmCyScreen) * Win32.UserDefaultScreenDpi / (float)Win32.GetDpiForSystem();

    /// <summary>The work area of the monitor with the device name, or null while no such monitor is attached.</summary>
    public static unsafe Win32.Rect? FindWorkArea(string? deviceName)
    {
        if (string.IsNullOrEmpty(deviceName)) return null;

        wantedDeviceName = deviceName;
        foundWorkArea = null;
        Win32.EnumDisplayMonitors(
            deviceContext: 0,
            clip: null,
            &OnMonitor,
            data: 0);
        return foundWorkArea;
    }

    /// <summary>
    /// Keeps a window inside a work area: the named monitor's while it is attached, else that of the monitor under
    /// the window's centre.
    /// </summary>
    public static (int X, int Y) ClampToWorkArea(
        double preferredX,
        double preferredY,
        int width,
        int height,
        string? monitor = null)
    {
        var center = new Win32.Point
        { X = ToDevicePixel(preferredX + width / 2d), Y = ToDevicePixel(preferredY + height / 2d) };

        var workArea = FindWorkArea(monitor) ?? GetWorkArea(center);
        return ClampToWorkArea(preferredX, preferredY, width, height, workArea);
    }

    public static (int X, int Y) ClampToWorkArea(
        double preferredX,
        double preferredY,
        int width,
        int height,
        Win32.Rect workArea)
    {
        width = Math.Max(width, MinimumDeviceLength);
        height = Math.Max(height, MinimumDeviceLength);
        int x = width >= workArea.Width
            ? workArea.Left
            : Math.Clamp(ToDevicePixel(preferredX), workArea.Left, workArea.Right - width);

        int y = height >= workArea.Height
            ? workArea.Top
            : Math.Clamp(ToDevicePixel(preferredY), workArea.Top, workArea.Bottom - height);

        return (x, y);
    }

    private static int ToDevicePixel(double value) =>
        (int)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), int.MinValue, int.MaxValue);

    private static nint FindMonitor(Win32.Point point) => Win32.MonitorFromPoint(point, Win32.MonitorDefaultToNearest);

    private static Win32.MonitorInfoEx GetInfo(nint monitor)
    {
        var info = new Win32.MonitorInfoEx { Size = (uint)Marshal.SizeOf<Win32.MonitorInfoEx>(), DeviceName = "" };
        Win32.GetMonitorInfo(monitor, ref info);
        return info;
    }

    [UnmanagedCallersOnly]
    private static unsafe int OnMonitor(
        nint monitor,
        nint deviceContext,
        Win32.Rect* bounds,
        nint data)
    {
        var info = GetInfo(monitor);
        if (!info.DeviceName.Equals(wantedDeviceName, StringComparison.OrdinalIgnoreCase)) return 1;

        foundWorkArea = info.WorkArea;
        return 0;
    }
}
