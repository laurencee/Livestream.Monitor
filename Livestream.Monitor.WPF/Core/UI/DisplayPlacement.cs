using System;
using System.Linq;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;

namespace Livestream.Monitor.WPF.Core.UI;

internal static class DisplayPlacement
{
    private static readonly Screen RequestedScreen = FindRequestedScreen();

    public static bool IsRequested => RequestedScreen != null;

    public static Rect GetWorkArea(Window window)
    {
        if (RequestedScreen == null) return SystemParameters.WorkArea;

        var area = RequestedScreen.WorkingArea;
        var dpi = VisualTreeHelper.GetDpi(window);
        return new Rect(
            x: area.Left / dpi.DpiScaleX,
            y: area.Top / dpi.DpiScaleY,
            width: area.Width / dpi.DpiScaleX,
            height: area.Height / dpi.DpiScaleY
        );
    }

    public static Rect GetNotificationBounds(
        Rect workArea,
        Size preferredSize,
        bool rightAligned,
        int row = 1)
    {
        const double Margin = 5;
        var columnWidth = Math.Max(0, workArea.Width / 2 - Margin * 2);
        var width = Math.Min(preferredSize.Width, columnWidth);
        var left = rightAligned ? workArea.Right - width - Margin : workArea.Left + Margin;
        var top = workArea.Bottom - preferredSize.Height * row - Margin;
        return new Rect(x: left, y: top, width: width, height: preferredSize.Height);
    }

    private static Screen FindRequestedScreen()
    {
        var args = Environment.GetCommandLineArgs();
        var index = Array.IndexOf(args, "--monitor");
        if (index < 0 || index + 1 >= args.Length) return null;

        var requested = args[index + 1];
        return Screen.AllScreens.FirstOrDefault(screen => screen.DeviceName.Equals(requested, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Display '{requested}' is not connected.");
    }
}
