using System.Drawing;
using Livestream.Monitor.Direct2D.Native;

namespace Livestream.Monitor.Direct2D.Widgets;

/// <summary>
/// A vertical scroll bar drawn and behaving as the WPF frontend's MahApps 2.4.11 scroll bar: the arrow buttons and the
/// track either side of the thumb repeat while held, and the thumb is dragged. Positions are in the owner's units,
/// such as rows or pixels; an arrow steps one line and the track one viewport.
/// </summary>
internal sealed class VerticalScrollBar(Action<int> scrollTo)
{
    public const float Width = 14;
    public const float MinimumDraggableHeight = ButtonLength * 2 + MinimumThumbLength + 1;

    // A 14-pixel arrow button with a 1-pixel vertical margin on each side.
    private const float ButtonLength = 16;
    private const float ArrowWidth = 12;
    private const float ArrowHeight = 6;
    private const float ThumbInset = 1;

    // The shortest thumb the MahApps scroll bar draws, measured from a long WPF list.
    private const float MinimumThumbLength = 25;

    private const uint ArrowColor = 0xFF9D9D9D;
    private const uint ThumbColor = 0xFFB9B9B9;
    private const uint Black = 0xFF000000;
    private const uint White = 0xFFFFFFFF;

    // MahApps Gray1, the hovered arrow color.
    private const uint DarkGray1 = 0xFFF9F9F9;
    private const uint LightGray1 = 0xFF333333;

    // A pressed arrow uses the accent at this opacity; the thumb is overlaid with the theme foreground.
    private const float PressedArrowOpacity = 0.8f;
    private const float HoveredThumbOpacity = 0.6f;
    private const float PressedThumbOpacity = 0.8f;

    private static readonly (int Delay, int Interval) RepeatTiming = ReadRepeatTiming();

    private RectangleF bounds;
    private RectangleF up;
    private RectangleF down;
    private RectangleF track;
    private RectangleF thumb;
    private int position;
    private int pageSize;
    private int lineStep;
    private int maximum;
    private Part pressed;
    private Part hovered;
    private float dragOffset;
    private long repeatAt;
    private PointF pointer;

    private enum Part
    {
        None,
        LineUp,
        LineDown,
        PageUp,
        PageDown,
        Thumb,
    }

    /// <summary>True when the content does not fit the viewport, so the bar is shown.</summary>
    public bool IsVisible => maximum > 0;

    public bool IsInteracting => pressed != Part.None;
    public RectangleF Bounds => bounds;

    /// <summary>Places the bar for content of the extent, of which the viewport is shown from the offset.</summary>
    public void Update(
        RectangleF area,
        int extent,
        int viewport,
        int offset,
        int lineSize = 1)
    {
        bounds = area;
        pageSize = viewport;
        lineStep = lineSize;
        maximum = Math.Max(0, extent - viewport);
        position = Math.Clamp(offset, 0, maximum);
        up = area with { Height = ButtonLength };
        down = area with { Y = area.Bottom - ButtonLength, Height = ButtonLength };
        track = area with { Y = up.Bottom, Height = Math.Max(0, down.Top - up.Bottom) };
        var ratio = Math.Min(1, viewport / (float)Math.Max(1, extent));
        var length = Math.Min(track.Height, Math.Max(MinimumThumbLength, track.Height * ratio));
        var travel = track.Height - length;
        var top = track.Y + (maximum == 0 ? 0 : travel * position / maximum);
        thumb = new RectangleF(x: area.X + ThumbInset, y: top, width: Width - ThumbInset * 2, height: length);
    }

    public void Draw(SurfaceUi ui, string? automationPrefix = null)
    {
        if (!IsVisible) return;

        var palette = ui.Palette;
        var hoveredArrow = palette.IsDark ? DarkGray1 : LightGray1;
        var pressedArrow = Theme.Blend(palette.Background, palette.Accent, PressedArrowOpacity);
        Arrow(up, Part.LineUp, pointsUp: true);
        Arrow(down, Part.LineDown, pointsUp: false);

        // A dragged thumb stays pressed wherever the pointer goes, as the captured WPF thumb does.
        var overlay = palette.IsDark ? White : Black;
        var opacity = pressed == Part.Thumb ? PressedThumbOpacity : hovered == Part.Thumb ? HoveredThumbOpacity : 0;
        ui.Fill(thumb, Theme.Blend(ThumbColor, overlay, opacity));

        if (automationPrefix != null)
        {
            ui.AddAutomationButton(
                id: automationPrefix + ".scrollup",
                name: "Scroll up",
                bounds: up,
                action: () => Step(Part.LineUp),
                enabled: position > 0
            );
            ui.AddAutomationButton(
                id: automationPrefix + ".scrolldown",
                name: "Scroll down",
                bounds: down,
                action: () => Step(Part.LineDown),
                enabled: position < maximum
            );
        }

        // A held arrow shows pressed only while the pointer is on it, as a WPF repeat button does.
        void Arrow(RectangleF button, Part part, bool pointsUp)
        {
            var left = button.X + (button.Width - ArrowWidth) / 2;
            var top = button.Y + (button.Height - ArrowHeight) / 2;
            var arrow = new RectangleF(x: left, y: top, width: ArrowWidth, height: ArrowHeight);
            var color = hovered != part ? ArrowColor : pressed == part ? pressedArrow : hoveredArrow;
            ui.Triangle(arrow, color, pointsUp);
        }
    }

    /// <summary>Starts a press on the bar; false when the point is not on it.</summary>
    public bool MouseDown(PointF point, long now)
    {
        if (!IsVisible || !bounds.Contains(point)) return false;

        pointer = point;
        pressed = PartAt(point);
        hovered = pressed;

        // As WPF's repeat buttons do, the first step happens on press and the repeats follow after a delay.
        if (pressed == Part.Thumb) dragOffset = point.Y - thumb.Top;
        else Step(pressed);

        repeatAt = now + RepeatTiming.Delay;
        return true;
    }

    /// <summary>Follows the pointer; true when the bar's appearance or position changed.</summary>
    public bool MouseMove(PointF point)
    {
        pointer = point;

        if (pressed == Part.Thumb)
        {
            var travel = track.Height - thumb.Height;
            var ratio = travel <= 0 ? 0 : (point.Y - track.Top - dragOffset) / travel;
            Scroll((int)MathF.Round(Math.Clamp(ratio, 0, 1) * maximum));
            return true;
        }

        var next = IsVisible && bounds.Contains(point) ? PartAt(point) : Part.None;
        if (next == hovered) return false;

        hovered = next;
        return true;
    }

    public void MouseUp()
    {
        pressed = Part.None;
        hovered = IsVisible && bounds.Contains(pointer) ? PartAt(pointer) : Part.None;
    }

    /// <summary>Repeats a held step while the pointer stays on its part; true when it scrolled.</summary>
    public bool Repeat(long now)
    {
        if (pressed is Part.None or Part.Thumb || now < repeatAt) return false;

        repeatAt = now + RepeatTiming.Interval;
        if (!bounds.Contains(pointer) || PartAt(pointer) != pressed) return false;

        Step(pressed);
        return true;
    }

    private Part PartAt(PointF point)
    {
        if (point.Y < up.Bottom) return Part.LineUp;
        if (point.Y >= down.Top) return Part.LineDown;
        if (point.Y < thumb.Top) return Part.PageUp;

        return point.Y < thumb.Bottom ? Part.Thumb : Part.PageDown;
    }

    private void Step(Part part)
    {
        var delta = part switch
        {
            Part.LineUp => -lineStep,
            Part.LineDown => lineStep,
            Part.PageUp => -pageSize,
            Part.PageDown => pageSize,
            _ => 0,
        };

        Scroll(position + delta);
    }

    private void Scroll(int target)
    {
        var next = Math.Clamp(target, 0, maximum);
        if (next == position) return;

        position = next;
        scrollTo(next);
    }

    // The keyboard repeat delay and rate, converted to milliseconds as WPF's RepeatButton converts them.
    private static (int Delay, int Interval) ReadRepeatTiming()
    {
        const uint GetKeyboardSpeed = 0x000A;
        const uint GetKeyboardDelay = 0x0016;
        const int MaximumDelay = 3;
        const int MaximumSpeed = 31;
        const int DelayStepMilliseconds = 250;
        const int SlowestIntervalMilliseconds = 400;
        const int FastestIntervalMilliseconds = 1000 / 30;

        if (!Win32.SystemParametersInfo(GetKeyboardDelay, parameter: 0, out var delay, update: 0) ||
            delay is < 0 or > MaximumDelay)
        {
            delay = 0;
        }

        if (!Win32.SystemParametersInfo(GetKeyboardSpeed, parameter: 0, out var speed, update: 0) ||
            speed is < 0 or > MaximumSpeed)
        {
            speed = MaximumSpeed;
        }

        var interval = (MaximumSpeed - speed) * (SlowestIntervalMilliseconds - FastestIntervalMilliseconds) /
                       MaximumSpeed + FastestIntervalMilliseconds;

        return ((delay + 1) * DelayStepMilliseconds, interval);
    }
}
