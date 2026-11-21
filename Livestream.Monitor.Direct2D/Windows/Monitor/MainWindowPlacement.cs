using Livestream.Monitor.Core.UI;
using Livestream.Monitor.Direct2D.Native;
using static Livestream.Monitor.Direct2D.Native.Win32;

namespace Livestream.Monitor.Direct2D.Windows.Monitor;

internal sealed class MainWindowPlacement
{
    private const int MinimumVisibleCaptionWidth = 64;
    private const int MinimumVisibleCaptionHeight = 16;
    private const uint MonitorDefaultToNull = 0;

    private readonly nint window;
    private readonly WindowLocation? savedLocation;
    private readonly bool usedFallback;
    private WindowLocation? location;

    public MainWindowPlacement(
        nint window,
        int titleHeight,
        int topTitleInset,
        int leftTitleInset,
        int rightTitleInset)
    {
        this.window = window;
        usedFallback = !WindowLocationFile.TryLoad(Program.WindowLocationPath, out savedLocation);
        if (!usedFallback && savedLocation is { } saved)
            usedFallback = !TryRestore(saved, titleHeight, topTitleInset, leftTitleInset, rightTitleInset);

        Capture();
    }

    public void Capture()
    {
        if (!IsIconic(window) && !IsZoomed(window) && GetWindowRect(window, out var bounds)) location = new WindowLocation(bounds.Left, bounds.Top);
    }

    public void Save()
    {
        if (!usedFallback && location is { } current && current != savedLocation) WindowLocationFile.Save(Program.WindowLocationPath, current);
    }

    private bool TryRestore(
        WindowLocation saved,
        int titleHeight,
        int topTitleInset,
        int leftTitleInset,
        int rightTitleInset)
    {
        if (!GetWindowRect(window, out var bounds)) return false;

        nint destination = FindSavedMonitor(bounds, MonitorDefaultToNearest);
        if (destination == 0) return false;

        if (TryRestoreOnMonitor(destination)) return true;

        // The startup size can point to the wrong display when the saved window used another DPI.
        bool restored = false;
        EnumDisplayMonitors(deviceContext: 0, clip: 0, TryOtherMonitor, data: 0);
        if (!restored) RestoreDefaultBounds(bounds);

        return restored;

        bool TryOtherMonitor(
            nint monitor,
            nint _,
            nint __,
            nint ___)
        {
            if (monitor != destination) restored = TryRestoreOnMonitor(monitor);

            return !restored;
        }

        bool TryRestoreOnMonitor(nint monitor)
        {
            if (!GetWindowRect(window, out var current)) return false;

            // Let the hidden window resize for this display before trying the saved coordinates.
            if (MonitorFromRect(in current, MonitorDefaultToNearest) != monitor)
            {
                if (!Monitors.TryGetInfo(monitor, out var info)) return false;

                var work = info.WorkArea;
                int x = work.Left + (work.Width - current.Width) / 2;
                int y = work.Top + (work.Height - current.Height) / 2;
                if (!Move(x, y) || !GetWindowRect(window, out current)) return false;
            }

            if (FindSavedMonitor(current, MonitorDefaultToNull) == 0) return false;

            return TryMoveToPosition(saved.X, saved.Y, out var placed) && HasReachableTitle(placed);
        }

        nint FindSavedMonitor(Rect size, uint flags)
        {
            int width = size.Width;
            int height = size.Height;
            if (saved.X > int.MaxValue - width || saved.Y > int.MaxValue - height) return 0;

            var target = new Rect { Left = saved.X, Top = saved.Y, Right = saved.X + width, Bottom = saved.Y + height };
            return MonitorFromRect(in target, flags);
        }

        bool HasReachableTitle(Rect placed)
        {
            double scale = GetDpiForWindow(window) / (double)UserDefaultScreenDpi;
            var title = new Rect
            {
                Left = placed.Left + (int)Math.Ceiling(leftTitleInset * scale),
                Top = placed.Top + (int)Math.Ceiling(topTitleInset * scale),
                Right = placed.Right - (int)Math.Ceiling(rightTitleInset * scale),
                Bottom = placed.Top + (int)Math.Floor(titleHeight * scale),
            };

            if (title.Right <= title.Left || title.Bottom <= title.Top) return false;

            bool reachable = false;
            EnumDisplayMonitors(deviceContext: 0, clip: 0, CheckWorkArea, data: 0);
            return reachable;

            bool CheckWorkArea(
                nint monitor,
                nint _,
                nint __,
                nint ___)
            {
                if (!Monitors.TryGetInfo(monitor, out var info)) return true;

                var workArea = info.WorkArea;
                int visibleWidth = Math.Min(title.Right, workArea.Right) - Math.Max(title.Left, workArea.Left);
                int visibleHeight = Math.Min(title.Bottom, workArea.Bottom) - Math.Max(title.Top, workArea.Top);
                reachable = visibleWidth >= MinimumVisibleCaptionWidth && visibleHeight >= MinimumVisibleCaptionHeight;
                return !reachable;
            }
        }
    }

    private bool TryMoveToPosition(int x, int y, out Rect bounds)
    {
        bounds = default;
        if (!Move(x, y) || !GetWindowRect(window, out bounds)) return false;
        if (bounds.Left == x && bounds.Top == y) return true;

        // A DPI change can apply Windows' suggested position during the first move.
        return Move(x, y) && GetWindowRect(window, out bounds) && bounds.Left == x && bounds.Top == y;
    }

    private void RestoreDefaultBounds(Rect original)
    {
        bool moved = TryMoveToPosition(original.Left, original.Top, out var bounds);
        if (moved && bounds.Right == original.Right && bounds.Bottom == original.Bottom) return;

        SetWindowPos(window, insertAfter: 0, x: original.Left, y: original.Top, original.Width, original.Height, flags: SwpNoZOrder | SwpNoActivate);
    }

    private bool Move(int x, int y) =>
        SetWindowPos(window, insertAfter: 0, x, y, width: 0, height: 0, flags: SwpNoSize | SwpNoZOrder | SwpNoActivate);
}
