using System.Drawing;

namespace Livestream.Monitor.Direct2D.Rendering;

/// <summary>
/// Aligns logical-unit geometry to whole device pixels, so edges are drawn solid instead of antialiased across two
/// pixels at fractional display scales (for example 1 logical pixel at 150% covering 1.5 device pixels).
/// </summary>
internal static class PixelSnap
{
    // Spans narrower than this many device pixels keep a consistent snapped width wherever they are placed; wider
    // spans snap both edges, so adjacent areas neither overlap nor leave gaps.
    private const float ThinSpanPixels = 3;

    /// <summary>The rectangle with each edge on the device pixel grid, in logical units.</summary>
    public static RectangleF Bounds(RectangleF bounds, float scale)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return bounds;

        var (x, width) = Span(bounds.X, bounds.Width, scale);
        var (y, height) = Span(bounds.Y, bounds.Height, scale);
        return new RectangleF(x, y, width, height);
    }

    /// <summary>A line or border thickness rounded to whole device pixels (at least one), in logical units.</summary>
    public static float Thickness(float thickness, float scale) => DevicePixels(thickness * scale) / scale;

    /// <summary>
    /// The centre of a line of the thickness across the coordinate, placed so both edges are on the pixel grid.
    /// </summary>
    public static float LineCentre(float coordinate, float thickness, float scale)
    {
        var pixels = DevicePixels(thickness * scale);
        var start = MathF.Round(coordinate * scale - pixels / 2f);
        return (start + pixels / 2f) / scale;
    }

    private static (float Start, float Length) Span(float start, float length, float scale)
    {
        var deviceStart = start * scale;
        var deviceLength = length * scale;

        if (deviceLength < ThinSpanPixels)
            return (MathF.Round(deviceStart) / scale, DevicePixels(deviceLength) / scale);

        var first = MathF.Round(deviceStart);
        var last = MathF.Round(deviceStart + deviceLength);
        return (first / scale, (last - first) / scale);
    }

    // Halves round down so a 1-pixel border at 150% stays one device pixel instead of becoming two.
    private static float DevicePixels(float length) => MathF.Max(1, MathF.Ceiling(length - 0.5f));
}
