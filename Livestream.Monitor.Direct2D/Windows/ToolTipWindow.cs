using System.Drawing;
using Livestream.Monitor.Direct2D.Native;
using Livestream.Monitor.Direct2D.Rendering;
using Livestream.Monitor.Direct2D.Widgets;

namespace Livestream.Monitor.Direct2D.Windows;

/// <summary>
/// A click-through popup showing hover text below the pointer, styled as the WPF frontend's MahApps tooltip: theme
/// background and text, a Gray7 border, 6 by 3 padding, and lines broken only at line feeds.
/// </summary>
internal sealed class ToolTipWindow : PopupWindow
{
    private const float FontSize = 12;
    private const float BorderThickness = 1;
    private const float HorizontalPadding = 6;
    private const float VerticalPadding = 3;

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

        // Moved first, so a different monitor's DPI applies before the size is calculated.
        var x = pointer.X;
        var y = pointer.Y + PointerOffset;
        const uint MoveFlags = Win32.SwpNoSize | Win32.SwpNoZOrder | Win32.SwpNoActivate;
        Win32.SetWindowPos(Handle, insertAfter: 0, x, y, width: 0, height: 0, MoveFlags);

        var contentWidth = MeasureWidth(text, FontSize) + (HorizontalPadding + BorderThickness) * 2;
        var lines = text.Count(character => character == '\n') + 1;
        var contentHeight = Painter.LineHeight(FontSize) * lines + (VerticalPadding + BorderThickness) * 2;
        var width = (int)MathF.Ceiling(contentWidth * Scale);
        var height = (int)MathF.Ceiling(contentHeight * Scale);

        // As in WPF, a popup that does not fit below the pointer opens above it.
        var workArea = Monitors.GetWorkArea(pointer);
        if (y + height > workArea.Bottom) y = pointer.Y - 1 - height;
        var position = Monitors.ClampToWorkArea(x, y, width, height);
        const uint ShowFlags = Win32.SwpNoActivate | Win32.SwpFrameChanged;
        nint topmost = -1;
        Win32.SetWindowPos(Handle, topmost, position.X, position.Y, width, height, ShowFlags);

        // Shown first: hidden windows are not redrawn, so the previous text would otherwise flash briefly.
        Show();
        Tick();
    }

    protected override void Draw(SizeF size)
    {
        var inset = HorizontalPadding + BorderThickness;
        var textBounds = new RectangleF(x: inset, y: 0, width: size.Width - inset * 2, height: size.Height);
        Ui.Text(text, textBounds, Ui.Palette.Text, size: FontSize);
    }
}
