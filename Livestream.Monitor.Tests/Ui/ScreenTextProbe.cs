using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using Livestream.Monitor.Direct2D.Native;

namespace Livestream.Monitor.Tests.Ui;

internal static class ScreenTextProbe
{
    public static nint FindVisibleReadOnlyEdit(nint parent, string className)
    {
        nint edit = 0;
        while ((edit = FindWindowEx(parent, edit, className, null)) != 0)
            if ((Win32.GetWindowLongPtr(edit, Win32.GwlStyle) & Win32.EsReadOnly) != 0 && Win32.IsWindowVisible(edit))
                return edit;

        return 0;
    }

    public static bool HasPaintedText(nint window, nint edit, out string observation)
    {
        observation = "No visible read-only edit control was found.";
        if (edit == 0) return false;

        // Capture physical pixels even if the window is on a differently scaled monitor.
        const int PerMonitorAwareV2 = -4;
        var previousDpi = SetThreadDpiAwarenessContext(PerMonitorAwareV2);
        if (previousDpi == 0) throw new Win32Exception(Marshal.GetLastWin32Error());

        try
        {
            observation = "The edit control had no screen area.";
            if (!Win32.GetWindowRect(edit, out var bounds) || bounds.Width <= 0 || bounds.Height <= 0) return false;

            const int EdgeInset = 2;
            var area = Rectangle.FromLTRB(
                bounds.Left + EdgeInset,
                bounds.Top + EdgeInset,
                bounds.Right - EdgeInset,
                bounds.Bottom - EdgeInset);
            if (area.Width <= 0 || area.Height <= 0) return false;

            observation = $"The edit control was obscured at {area}.";
            if (!IsUnobscured(area)) return false;

            using var bitmap = new Bitmap(area.Width, area.Height);
            using var graphics = Graphics.FromImage(bitmap);

            graphics.CopyFromScreen(area.Location, Point.Empty, area.Size);
            if (!IsUnobscured(area)) return false;

            // A row through multiple glyphs has repeated contrast transitions. A flat area with an edge can have contrast too.
            var background = bitmap.GetPixel(area.Width - 1, area.Height - 1).GetBrightness();
            const float MinimumContrast = 0.2f;
            const int MinimumTransitions = 6;
            for (var y = 0; y < area.Height; y++)
            {
                var previousContrasts = false;
                var transitions = 0;
                for (var x = 0; x < area.Width; x++)
                {
                    var contrasts = Math.Abs(bitmap.GetPixel(x, y).GetBrightness() - background) >= MinimumContrast;
                    if (contrasts != previousContrasts && ++transitions >= MinimumTransitions) return true;

                    previousContrasts = contrasts;
                }
            }

            observation = $"No rows with text-like contrast in {area}.";
            return false;
        }
        finally
        {
            SetThreadDpiAwarenessContext(previousDpi);
        }

        bool IsUnobscured(Rectangle area) =>
            IsAt(area.Left, area.Top) && IsAt(area.Right - 1, area.Top) &&
            IsAt(area.Left, area.Bottom - 1) && IsAt(area.Right - 1, area.Bottom - 1) &&
            IsAt(area.Left + area.Width / 2, area.Top + area.Height / 2);

        bool IsAt(int x, int y)
        {
            var hit = Win32.WindowFromPoint(new Win32.Point { X = x, Y = y });
            return hit == window || hit == edit;
        }
    }

    [DllImport("user32.dll", EntryPoint = "FindWindowExW", CharSet = CharSet.Unicode)]
    private static extern nint FindWindowEx(
        nint parent,
        nint after,
        string className,
        string title);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetThreadDpiAwarenessContext(nint dpiContext);
}
