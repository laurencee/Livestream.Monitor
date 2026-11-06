using System.Drawing;
using Livestream.Monitor.Direct2D.Native;
using Livestream.Monitor.Direct2D.Widgets;

namespace Livestream.Monitor.Direct2D.Windows;

/// <summary>
/// A click-through popup showing hover text below the pointer, styled as the WPF frontend's MahApps tooltip: theme
/// background and text, a Gray7 border, and 6 by 3 padding.
/// </summary>
internal sealed class ToolTipWindow : PopupWindow
{
    private const float FontSize = 12;
    private const float BorderThickness = 1;
    private const float HorizontalInset = 6 + BorderThickness;
    private const float VerticalInset = 3 + BorderThickness;
    private const float MaximumTextWidth = 480;

    // WPF places mouse-anchored popups 17 device pixels below the pointer hot spot, at its x coordinate; measured
    // from the WPF frontend.
    private const int PointerOffset = 17;

    private string text = "";

    public ToolTipWindow(nint owner, Func<ThemePalette> theme)
        : base(title: "Tooltip", width: 1, height: 1, owner: owner)
    {
        ThemeSource = theme;
    }

    protected override uint? WindowBorderColor => ThemeSource().ToolTipBorder;

    public void Display(string value, Win32.Point pointer)
    {
        if (text != value)
        {
            text = value;
            NeedsPaint = true;
        }

        var workArea = Monitors.GetWorkArea(pointer);
        // The old popup size can straddle a monitor boundary and select the wrong DPI.
        var monitorX = workArea.Left + workArea.Width / 2;
        var monitorY = workArea.Top + workArea.Height / 2;
        const uint MoveFlags = Win32.SwpNoZOrder | Win32.SwpNoActivate;
        Win32.SetWindowPos(Handle, insertAfter: 0, x: monitorX, y: monitorY, width: 1, height: 1, flags: MoveFlags);

        var textWidth = Math.Min(MeasureWidth(text, FontSize), MaximumTextWidth);
        var width = Math.Min(workArea.Width, (int)MathF.Ceiling((textWidth + HorizontalInset * 2) * Scale));
        var textHeight = MeasureWrapped(text, width / Scale - HorizontalInset * 2, FontSize);
        var height = Math.Min(workArea.Height, (int)MathF.Ceiling((textHeight + VerticalInset * 2) * Scale));

        // As in WPF, a popup that does not fit below the pointer opens above it.
        var x = Math.Clamp(pointer.X, workArea.Left, workArea.Right - width);
        var y = pointer.Y + PointerOffset;
        if (y + height > workArea.Bottom) y = pointer.Y - 1 - height;
        y = Math.Clamp(y, workArea.Top, workArea.Bottom - height);

        const uint ShowFlags = Win32.SwpNoActivate | Win32.SwpFrameChanged;
        nint topmost = -1;
        Win32.SetWindowPos(Handle, topmost, x, y, width, height, ShowFlags);

        // Shown first: hidden windows are not redrawn, so the previous text would otherwise flash briefly.
        Show();
        Tick();
    }

    protected override void Draw(SizeF size)
    {
        var bounds = new RectangleF(PointF.Empty, size);
        var textBounds = RectangleF.Inflate(bounds, -HorizontalInset, -VerticalInset);
        Ui.Text(text, textBounds, Ui.Palette.Text, size: FontSize, wrap: true);
    }
}
