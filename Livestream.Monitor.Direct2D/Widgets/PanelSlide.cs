using System.Diagnostics;

namespace Livestream.Monitor.Direct2D.Widgets;

internal sealed class PanelSlide
{
    public const int DurationMilliseconds = 250;
    private float startFraction;
    private double started;

    public static double NowMilliseconds => Stopwatch.GetTimestamp() * (1000d / Stopwatch.Frequency);

    public float VisibleFraction { get; private set; }
    public bool IsOpen { get; private set; }
    public bool IsAnimating { get; private set; }

    public void SetOpen(bool open, double now)
    {
        Tick(now);
        if (IsOpen == open) return;

        IsOpen = open;
        startFraction = VisibleFraction;
        started = now;
        IsAnimating = open ? VisibleFraction < 1 : VisibleFraction > 0;
    }

    public bool Tick(double now)
    {
        if (!IsAnimating) return false;

        var progress = (float)Math.Clamp((now - started) / DurationMilliseconds, 0, 1);
        var remaining = 1 - progress;
        var eased = 1 - remaining * remaining * remaining;
        var target = IsOpen ? 1 : 0;
        VisibleFraction = startFraction + (target - startFraction) * eased;
        IsAnimating = progress < 1;
        return true;
    }
}
