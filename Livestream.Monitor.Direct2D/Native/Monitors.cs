using System.Runtime.InteropServices;

namespace Livestream.Monitor.Direct2D.Native;

/// <summary>Monitor lookups in device pixels, for saved window placement.</summary>
internal static class Monitors
{
    private const int MinimumDeviceLength = 1;

    public static Win32.Rect GetWorkArea(Win32.Point point) => GetMonitorWorkArea(Win32.MonitorFromPoint(point, Win32.MonitorDefaultToNearest));

    // Windows uses the pre-minimize bounds to find the monitor of a minimized window.
    public static Win32.Rect GetWindowWorkArea(nint window) => GetMonitorWorkArea(Win32.MonitorFromWindow(window, Win32.MonitorDefaultToNearest));

    /// <summary>The primary monitor's height in logical units at the system DPI.</summary>
    public static float PrimaryScreenHeight =>
        Win32.GetSystemMetrics(Win32.SmCyScreen) * Win32.UserDefaultScreenDpi / (float)Win32.GetDpiForSystem();

    /// <summary>The work area of the monitor with the device name, or null while no such monitor is attached.</summary>
    public static Win32.Rect? FindWorkArea(string? deviceName)
    {
        if (string.IsNullOrEmpty(deviceName)) return null;

        Win32.Rect? foundWorkArea = null;
        Win32.EnumDisplayMonitors(deviceContext: 0, clip: 0, FindDevice, data: 0);
        return foundWorkArea;

        bool FindDevice(
            nint monitor,
            nint _,
            nint __,
            nint ___)
        {
            if (!TryGetInfo(monitor, out var info) || !deviceName.Equals(info.DeviceName, StringComparison.OrdinalIgnoreCase)) return true;

            foundWorkArea = info.WorkArea;
            return false;
        }
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

    internal static bool TryGetInfo(nint monitor, out Win32.MonitorInfoEx info)
    {
        info = new Win32.MonitorInfoEx { Size = (uint)Marshal.SizeOf<Win32.MonitorInfoEx>(), DeviceName = "" };
        return Win32.GetMonitorInfo(monitor, ref info);
    }

    private static Win32.Rect GetMonitorWorkArea(nint monitor) => TryGetInfo(monitor, out var info) ? info.WorkArea : default;
}
