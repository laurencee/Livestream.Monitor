using System.Drawing;
using Livestream.Monitor.Direct2D.Rendering;

namespace Livestream.Monitor.Direct2D.Widgets;

internal sealed class ActionFeedback
{
    private const int HoldMilliseconds = 1_250;
    private const int FadeMilliseconds = 250;
    private long started;
    private string message = "";
    private PointF position;

    public float Opacity { get; private set; }
    public bool IsVisible => Opacity > 0;

    public void Show(string text, PointF clickPosition, long now)
    {
        message = text;
        position = clickPosition;
        started = now;
        Opacity = 1;
    }

    public bool Tick(long now)
    {
        if (!IsVisible) return false;

        var elapsed = now - started;
        Opacity = 1 - Math.Clamp((elapsed - HoldMilliseconds) / (float)FadeMilliseconds, 0, 1);
        return true;
    }

    public void Draw(SurfaceUi ui, RectangleF viewport)
    {
        if (!IsVisible) return;

        const float Margin = 4;
        const float HorizontalPadding = 12;
        const float Height = 32;
        const float CornerRadius = 4;
        const float FontSize = 12;
        var width = Math.Min(ui.MeasureWidth(message, FontSize) + HorizontalPadding * 2, viewport.Width - Margin * 2);
        var height = Math.Min(Height, viewport.Height - Margin * 2);
        if (width <= 0 || height <= 0) return;

        var bounds = new RectangleF(
            x: Math.Clamp(position.X, viewport.Left + Margin, viewport.Right - Margin - width),
            y: Math.Clamp(position.Y, viewport.Top + Margin, viewport.Bottom - Margin - height),
            width: width,
            height: height);

        ui.Fill(bounds, Fade(ui.Palette.Panel), CornerRadius);
        ui.Border(bounds, Fade(ui.Palette.Border), radius: CornerRadius);

        var textBounds = bounds with { X = bounds.X + HorizontalPadding, Width = Math.Max(0, width - HorizontalPadding * 2) };
        ui.Text(message, textBounds, Fade(ui.Palette.Text), size: FontSize, alignment: TextAlign.Center);
    }

    private uint Fade(uint color)
    {
        const int AlphaShift = 24;
        const uint RgbMask = 0x00FFFFFF;
        var alpha = (uint)MathF.Round((color >> AlphaShift) * Opacity);
        return (color & RgbMask) | (alpha << AlphaShift);
    }
}
