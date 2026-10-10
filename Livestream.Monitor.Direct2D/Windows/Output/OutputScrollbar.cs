using System.Drawing;
using Livestream.Monitor.Direct2D.Widgets;

namespace Livestream.Monitor.Direct2D.Windows.Output;

/// <summary>The output scrollbar has one fixed visual state, matching the expanded Windows hover appearance.</summary>
internal sealed class OutputScrollbar(Action<int> scrollTo)
{
    public const float Width = 17;
    private const float ThumbWidth = 6;
    private const float MinimumThumbHeight = 18;
    private const float ArrowFontSize = 11;
    private const uint TrackColor = 0xFFF0F0F0;
    private const uint InkColor = 0xFF858585;
    private const int InitialRepeatMilliseconds = 350;
    private const int RepeatMilliseconds = 70;
    private RectangleF bounds;
    private RectangleF track;
    private RectangleF thumb;
    private int firstLine;
    private int visibleLines;
    private int maximum;
    private bool dragging;
    private float dragOffset;
    private int repeatDirection;
    private long repeatAt;

    public bool IsInteracting => dragging || repeatDirection != 0;

    public void Update(
        RectangleF area,
        int lines,
        int visible,
        int first)
    {
        bounds = area;
        visibleLines = visible;
        maximum = Math.Max(0, lines - visible);
        firstLine = Math.Clamp(first, 0, maximum);
        track = area with { Y = area.Y + Width, Height = Math.Max(0, area.Height - Width * 2) };
        var ratio = Math.Min(1, visible / (float)Math.Max(1, lines));
        var height = Math.Min(track.Height, Math.Max(MinimumThumbHeight, track.Height * ratio));
        var travel = track.Height - height;
        var top = track.Y + (maximum == 0 ? 0 : travel * firstLine / maximum);
        thumb = new RectangleF(x: area.X + (Width - ThumbWidth) / 2, y: top, width: ThumbWidth, height: height);
    }

    public void Draw(SurfaceUi ui)
    {
        ui.Fill(bounds, TrackColor);
        ui.Fill(thumb, InkColor, radius: ThumbWidth / 2);
        var up = bounds with { Height = Width };
        var down = bounds with { Y = bounds.Bottom - Width, Height = Width };
        ui.Icon(text: "▲", up, InkColor, size: ArrowFontSize);
        ui.Icon(text: "▼", down, InkColor, size: ArrowFontSize);
    }

    public bool MouseDown(PointF point)
    {
        if (!bounds.Contains(point)) return false;
        if (maximum == 0) return true;

        if (point.Y >= thumb.Top && point.Y < thumb.Bottom && track.Contains(point))
        {
            dragging = true;
            dragOffset = point.Y - thumb.Top;
        }
        else if (point.Y < track.Top || point.Y >= track.Bottom)
        {
            repeatDirection = point.Y < track.Top ? -1 : 1;
            Scroll(firstLine + repeatDirection);
            repeatAt = Environment.TickCount64 + InitialRepeatMilliseconds;
        }
        else
        {
            var direction = point.Y < thumb.Top ? -1 : 1;
            Scroll(firstLine + direction * visibleLines);
        }

        return true;
    }

    public void MouseMove(PointF point)
    {
        if (!dragging) return;

        var travel = track.Height - thumb.Height;
        var ratio = travel <= 0 ? 0 : (point.Y - track.Top - dragOffset) / travel;
        Scroll((int)Math.Round(Math.Clamp(ratio, 0, 1) * maximum));
    }

    public void MouseUp()
    {
        dragging = false;
        repeatDirection = 0;
    }

    public void Repeat(long now)
    {
        if (repeatDirection == 0 || now < repeatAt) return;

        Scroll(firstLine + repeatDirection);
        repeatAt = now + RepeatMilliseconds;
    }

    private void Scroll(int line)
    {
        var next = Math.Clamp(line, 0, maximum);
        if (next == firstLine) return;

        firstLine = next;
        scrollTo(next);
    }
}
