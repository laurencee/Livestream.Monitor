using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using Livestream.Monitor.Direct2D.Native;

namespace Livestream.Monitor.Direct2D.Rendering;

/// <summary>The DirectWrite font weights the overlay draws with.</summary>
internal enum FontWeight
{
    Normal = 400,
    SemiBold = 600,
    Bold = 700,
}

internal enum TextAlign : byte
{
    Leading,
    Center,
    Trailing,
}

/// <summary>
/// How text is laid out: size in logical units, weight and alignment; its colour is the command's. A single line that
/// overflows its box is trimmed with an ellipsis, or clipped when <paramref name="Trim"/> is false.
/// </summary>
internal readonly record struct TextStyle(
    float Size,
    FontWeight Weight = FontWeight.Normal,
    TextAlign Align = TextAlign.Leading,
    bool Trim = true,
    bool SystemSymbol = false);

internal enum DrawKind : byte
{
    Fill,
    Stroke,
    Line,
    Ellipse,

    /// <summary>A filled isosceles triangle in the bounds, its apex at the top centre.</summary>
    TriangleUp,

    /// <summary>A filled isosceles triangle in the bounds, its apex at the bottom centre.</summary>
    TriangleDown,
    Bitmap,
    Text,
    Icon,
    WrappedText,
    PushClip,
    PopClip,
}

/// <summary>
/// One drawing operation in logical units. A window's commands are kept after it is drawn, so an unchanged window is
/// recognised by comparing lists and not presented again.
/// </summary>
internal struct DrawCommand
{
    public DrawKind Kind;

    /// <summary>The rectangle drawn or clipped; a line runs from its top-left to its bottom-right.</summary>
    public RectangleF Bounds;

    public uint Color;
    public float Radius;
    public float Thickness;
    public string? Text;
    public TextStyle Style;
    public D2DBitmap? Bitmap;

    /// <summary>The bitmap's content version; a bitmap updated in place draws differently at a new revision.</summary>
    public int Revision;

    public readonly bool Matches(in DrawCommand other) =>
        Kind == other.Kind &&
        Bounds == other.Bounds &&
        Color == other.Color &&
        Radius.Equals(other.Radius) &&
        Thickness.Equals(other.Thickness) &&
        Text == other.Text &&
        Style == other.Style &&
        ReferenceEquals(Bitmap, other.Bitmap) &&
        Revision == other.Revision;
}

/// <summary>
/// Measures text and replays draw commands on the shared device context. Brushes are cached per device generation.
/// Text formats and laid-out text are device-independent; text layouts are reused across frames and released once
/// unused, as native DirectWrite renderers (Windows Terminal, Chromium) do instead of re-shaping text through
/// <c>DrawText</c> on every frame.
/// </summary>
internal sealed class Painter(GraphicsDevice graphics) : IDisposable
{
    private const string FontFamily = "Segoe UI";
    private const string FluentIconFontFamily = "Segoe Fluent Icons";
    private const string Mdl2IconFontFamily = "Segoe MDL2 Assets";

    // Segoe UI's design line spacing (ascent + descent over em).
    private const float SegoeUiLineSpacing = 1.33f;

    // Segoe UI's design ascent over em (2210 / 2048 font units).
    private const float SegoeUiAscent = 1.0791f;

    private const long LayoutRetentionMilliseconds = 2_000;
    private const long TrimIntervalMilliseconds = 1_000;

    private readonly Dictionary<uint, D2DBrush> brushes = [];
    private readonly Dictionary<FormatKey, TextFormat> formats = [];
    private readonly Dictionary<LayoutKey, CachedLayout> layouts = [];
    private readonly List<LayoutKey> expiredLayouts = [];
    private string? systemSymbolFont;
    private int brushGeneration;
    private float scale = 1;
    private long now;
    private long nextTrimMilliseconds;

    /// <summary>Line height of the font in logical units.</summary>
    public static float LineHeight(float size) => size * SegoeUiLineSpacing;

    /// <summary>Distance from the top of a line to its baseline in logical units.</summary>
    public static float Ascent(float size) => size * SegoeUiAscent;

    /// <summary>Starts a frame; about once a second it releases laid-out text that recent frames did not use.</summary>
    public void BeginFrame(long nowMilliseconds)
    {
        now = nowMilliseconds;
        if (now < nextTrimMilliseconds) return;

        nextTrimMilliseconds = now + TrimIntervalMilliseconds;

        foreach (var (key, layout) in layouts)
            if (now - layout.LastUsedMilliseconds > LayoutRetentionMilliseconds)
                expiredLayouts.Add(key);

        foreach (var key in expiredLayouts)
        {
            layouts[key].Layout.Dispose();
            layouts.Remove(key);
        }

        expiredLayouts.Clear();
    }

    // Measuring lays text out leading-aligned: in an unbounded box, centred or trailing text has no usable precision.

    /// <summary>Width of the unwrapped text in logical units.</summary>
    public float MeasureWidth(string text, in TextStyle style) =>
        GetLayout(
            text,
            Key(style, TextAlign.Leading, isWrapped: false),
            width: 0,
            height: 0).GetMetrics()
            .WidthIncludingTrailingWhitespace;

    /// <summary>Height of word-wrapped text in the given width, in logical units.</summary>
    public float MeasureWrapped(string text, float width, in TextStyle style) =>
        text.Length == 0
            ? 0
            : GetLayout(
                text,
                Key(style, TextAlign.Leading, isWrapped: true),
                width,
                height: 0).GetMetrics().Height;

    public float MeasureFirstTwoLines(string text, float width, in TextStyle style)
    {
        if (text.Length == 0) return 0;

        var format = Key(style, TextAlign.Leading, isWrapped: true);
        return GetLayout(text, format, width, height: 0).GetFirstTwoLinesHeight();
    }

    /// <summary>Draws the commands, in logical units scaled to device pixels, between BeginDraw and EndDraw.</summary>
    public void Render(D2DDeviceContext context, List<DrawCommand> commands, float pixelScale)
    {
        if (brushGeneration != graphics.Generation)
        {
            foreach (var brush in brushes.Values) brush.Dispose();

            brushes.Clear();
            brushGeneration = graphics.Generation;
        }

        scale = pixelScale;
        context.SetTransform(Matrix3x2.CreateScale(pixelScale));
        context.Clear(color: default);
        foreach (ref readonly var command in CollectionsMarshal.AsSpan(commands)) Draw(context, in command);

        context.SetTransform(Matrix3x2.Identity);
    }

    public void Dispose()
    {
        foreach (var brush in brushes.Values) brush.Dispose();
        foreach (var layout in layouts.Values) layout.Layout.Dispose();
        foreach (var format in formats.Values) format.Dispose();

        brushes.Clear();
        layouts.Clear();
        formats.Clear();
    }

    private void Draw(D2DDeviceContext context, in DrawCommand command)
    {
        var bounds = command.Bounds;

        // Straight edges sit on whole device pixels; text, ellipses and diagonal lines stay antialiased.
        var rect = D2DRect.From(PixelSnap.Bounds(bounds, scale));

        switch (command.Kind)
        {
            case DrawKind.Fill when command.Radius > 0:
                var rounded = new D2DRoundedRect(rect, command.Radius, command.Radius);
                context.FillRoundedRectangle(rounded, Brush(command.Color));
                break;
            case DrawKind.Fill:
                context.FillRectangle(rect, Brush(command.Color));
                break;
            case DrawKind.Stroke:
                DrawStroke(context, in command);
                break;
            case DrawKind.Line:
                var from = new Vector2(bounds.Left, bounds.Top);
                var to = new Vector2(bounds.Right, bounds.Bottom);
                context.DrawLine(
                    from,
                    to,
                    Brush(command.Color),
                    command.Thickness);
                break;
            case DrawKind.Ellipse:
                var center = new Vector2(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
                var ellipse = new D2DEllipse(center, bounds.Width / 2, bounds.Height / 2);
                context.FillEllipse(ellipse, Brush(command.Color));
                break;
            case DrawKind.TriangleUp:
            case DrawKind.TriangleDown:
                FillTriangle(context, in command);
                break;
            case DrawKind.Bitmap:
                context.DrawBitmap(command.Bitmap!, rect, opacity: 1);
                break;
            case DrawKind.Text:
            case DrawKind.Icon:
            case DrawKind.WrappedText:
                DrawText(context, in command);
                break;
            case DrawKind.PushClip:
                context.PushAxisAlignedClip(rect);
                break;
            case DrawKind.PopClip:
                context.PopAxisAlignedClip();
                break;
        }
    }

    // A border inside the bounds: strokes are centred on the geometry, so it is inset by half the thickness.
    private void DrawStroke(D2DDeviceContext context, in DrawCommand command)
    {
        var thickness = PixelSnap.Thickness(command.Thickness, scale);
        var outer = PixelSnap.Bounds(command.Bounds, scale);
        var inset = thickness / 2;
        var inner = D2DRect.From(RectangleF.Inflate(outer, -inset, -inset));

        if (command.Radius > 0)
        {
            // The stroke's outer edge then follows the fill's rounded corner.
            var radius = Math.Max(0, command.Radius - inset);
            var rounded = new D2DRoundedRect(inner, radius, radius);
            context.DrawRoundedRectangle(rounded, Brush(command.Color), thickness);
        }
        else
        {
            context.DrawRectangle(inner, Brush(command.Color), thickness);
        }
    }

    // The base sits on whole device pixels; the sloped sides stay antialiased.
    private void FillTriangle(D2DDeviceContext context, in DrawCommand command)
    {
        var bounds = PixelSnap.Bounds(command.Bounds, scale);
        var up = command.Kind == DrawKind.TriangleUp;
        var apex = new Vector2(bounds.X + bounds.Width / 2, up ? bounds.Top : bounds.Bottom);
        var baseY = up ? bounds.Bottom : bounds.Top;
        ReadOnlySpan<Vector2> corners = [new(bounds.Right, baseY), new(bounds.Left, baseY)];

        // Geometry is device-independent and cheap to build; arrows are only drawn when a frame changes.
        using var geometry = graphics.CreatePathGeometry();

        using (var sink = geometry.Open())
        {
            sink.AddPolygon(apex, corners);
            sink.Close();
        }

        context.FillGeometry(geometry, Brush(command.Color));
    }

    // Wrapped text from the top of the box, or one line vertically centred in it and trimmed with an ellipsis.
    private void DrawText(D2DDeviceContext context, in DrawCommand command)
    {
        var style = command.Style;
        var bounds = command.Bounds;
        var key = Key(style, style.Align, isWrapped: command.Kind == DrawKind.WrappedText);
        var layout = GetLayout(
            command.Text!,
            key,
            bounds.Width,
            bounds.Height);

        var origin = new Vector2(bounds.X, bounds.Y);

        if (command.Kind == DrawKind.Icon)
        {
            // Font line-box centering leaves symbols offset; center their visible ink instead.
            var ink = layout.GetOverhangMetrics();
            origin += new Vector2((ink.Left - ink.Right) / 2, (ink.Top - ink.Bottom) / 2);
        }

        context.DrawTextLayout(
            origin,
            layout,
            Brush(command.Color),
            isClipped: true);
    }

    private D2DBrush Brush(uint argb)
    {
        if (brushes.TryGetValue(argb, out var brush)) return brush;

        brush = graphics.Context.CreateSolidColorBrush(ToColor(argb));
        brushes.Add(argb, brush);
        return brush;
    }

    private DWriteTextLayout GetLayout(
        string text,
        FormatKey format,
        float width,
        float height)
    {
        var key = new LayoutKey(Text: text, Format: format, Width: width, Height: height);

        if (layouts.TryGetValue(key, out var cached))
        {
            cached.LastUsedMilliseconds = now;
            return cached.Layout;
        }

        // A zero box measures the natural width; drawing boxes clip and trim.
        float maximumWidth = width > 0 ? width : float.MaxValue;
        float maximumHeight = height > 0 ? height : float.MaxValue;
        var layout = graphics.WriteFactory.CreateTextLayout(
            text,
            GetFormat(format),
            maximumWidth,
            maximumHeight);

        layouts.Add(key, new CachedLayout(layout) { LastUsedMilliseconds = now });
        return layout;
    }

    private DWriteTextFormat GetFormat(FormatKey key)
    {
        if (formats.TryGetValue(key, out var format)) return format.Format;

        var family = key.SystemSymbol
            ? systemSymbolFont ??= graphics.WriteFactory.HasFontFamily(FluentIconFontFamily) ? FluentIconFontFamily : Mdl2IconFontFamily
            : FontFamily;
        var created = graphics.WriteFactory.CreateTextFormat(family, (int)key.Weight, key.Size);
        ComObject? ellipsis = null;

        try
        {
            created.SetWordWrapping(key.IsWrapped);
            var paragraph = key.IsWrapped ? DWriteParagraphAlignment.Near : DWriteParagraphAlignment.Center;
            created.SetParagraphAlignment(paragraph);
            var alignment = key.Align switch
            {
                TextAlign.Center => DWriteTextAlignment.Center,
                TextAlign.Trailing => DWriteTextAlignment.Trailing,
                _ => DWriteTextAlignment.Leading,
            };

            created.SetTextAlignment(alignment);

            // Wrapped text is clipped to its box; single lines are trimmed with an ellipsis unless clipped as well.
            if (key is { IsWrapped: false, Trim: true })
            {
                ellipsis = graphics.WriteFactory.CreateEllipsisTrimmingSign(created);
                created.SetCharacterTrimming(ellipsis);
            }
        }
        catch
        {
            ellipsis?.Dispose();
            created.Dispose();
            throw;
        }

        formats.Add(key, new TextFormat(created, ellipsis));
        return created;
    }

    private static D2DColor ToColor(uint argb)
    {
        const float ChannelMaximum = byte.MaxValue;
        return new D2DColor(
            red: ((argb >> 16) & 0xFF) / ChannelMaximum,
            green: ((argb >> 8) & 0xFF) / ChannelMaximum,
            blue: (argb & 0xFF) / ChannelMaximum,
            alpha: (argb >> 24) / ChannelMaximum);
    }

    private static FormatKey Key(in TextStyle style, TextAlign align, bool isWrapped) =>
        new(Size: style.Size, Weight: style.Weight, Align: align, IsWrapped: isWrapped, Trim: style.Trim, SystemSymbol: style.SystemSymbol);

    private readonly record struct FormatKey(
        float Size,
        FontWeight Weight,
        TextAlign Align,
        bool IsWrapped,
        bool Trim,
        bool SystemSymbol);

    // ReSharper disable NotAccessedPositionalProperty.Local -- All components participate in cache-key equality.
    private readonly record struct LayoutKey(
        string Text,
        FormatKey Format,
        float Width,
        float Height);
    // ReSharper restore NotAccessedPositionalProperty.Local

    private sealed class CachedLayout(DWriteTextLayout layout)
    {
        public DWriteTextLayout Layout { get; } = layout;

        public long LastUsedMilliseconds { get; set; }
    }

    private sealed class TextFormat(DWriteTextFormat format, ComObject? ellipsis) : IDisposable
    {
        public DWriteTextFormat Format { get; } = format;

        public void Dispose()
        {
            Format.Dispose();
            ellipsis?.Dispose();
        }
    }
}
