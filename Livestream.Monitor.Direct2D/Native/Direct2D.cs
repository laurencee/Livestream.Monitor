using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Livestream.Monitor.Direct2D.Native;

/// <summary>D2D1_COLOR_F, straight alpha.</summary>
internal readonly struct D2DColor(
    float red,
    float green,
    float blue,
    float alpha)
{
    public readonly float Red = red;
    public readonly float Green = green;
    public readonly float Blue = blue;
    public readonly float Alpha = alpha;
}

/// <summary>D2D_RECT_F.</summary>
internal readonly struct D2DRect(
    float left,
    float top,
    float right,
    float bottom)
{
    public readonly float Left = left;
    public readonly float Top = top;
    public readonly float Right = right;
    public readonly float Bottom = bottom;

    public static D2DRect From(RectangleF bounds) =>
        new(left: bounds.Left, top: bounds.Top, right: bounds.Right, bottom: bounds.Bottom);
}

/// <summary>D2D1_SIZE_U.</summary>
internal readonly struct D2DSizeU(uint width, uint height)
{
    public readonly uint Width = width;
    public readonly uint Height = height;
}

/// <summary>D2D1_RECT_U.</summary>
internal readonly struct D2DRectU(
    uint left,
    uint top,
    uint right,
    uint bottom)
{
    public readonly uint Left = left;
    public readonly uint Top = top;
    public readonly uint Right = right;
    public readonly uint Bottom = bottom;
}

/// <summary>D2D1_ROUNDED_RECT.</summary>
internal readonly struct D2DRoundedRect(D2DRect rect, float radiusX, float radiusY)
{
    public readonly D2DRect Rect = rect;
    public readonly float RadiusX = radiusX;
    public readonly float RadiusY = radiusY;
}

/// <summary>D2D1_ELLIPSE.</summary>
internal readonly struct D2DEllipse(Vector2 center, float radiusX, float radiusY)
{
    public readonly Vector2 Center = center;
    public readonly float RadiusX = radiusX;
    public readonly float RadiusY = radiusY;
}

/// <summary>D2D1_PIXEL_FORMAT.</summary>
internal readonly struct D2DPixelFormat(DxgiFormat format, uint alphaMode)
{
    public readonly DxgiFormat Format = format;
    public readonly uint AlphaMode = alphaMode;
}

/// <summary>D2D1_BITMAP_PROPERTIES1.</summary>
internal readonly struct D2DBitmapProperties1(
    D2DPixelFormat pixelFormat,
    float dpiX,
    float dpiY,
    uint options,
    nint colorContext)
{
    public readonly D2DPixelFormat PixelFormat = pixelFormat;
    public readonly float DpiX = dpiX;
    public readonly float DpiY = dpiY;
    public readonly uint Options = options;
    public readonly nint ColorContext = colorContext;
}

/// <summary>DWRITE_TEXT_METRICS.</summary>
internal readonly struct DWriteTextMetrics(
    float left,
    float top,
    float width,
    float widthIncludingTrailingWhitespace,
    float height,
    float layoutWidth,
    float layoutHeight,
    uint maxBidiReorderingDepth,
    uint lineCount)
{
    public readonly float Left = left;
    public readonly float Top = top;
    public readonly float Width = width;
    public readonly float WidthIncludingTrailingWhitespace = widthIncludingTrailingWhitespace;
    public readonly float Height = height;
    public readonly float LayoutWidth = layoutWidth;
    public readonly float LayoutHeight = layoutHeight;
    public readonly uint MaxBidiReorderingDepth = maxBidiReorderingDepth;
    public readonly uint LineCount = lineCount;
}

/// <summary>DWRITE_LINE_METRICS.</summary>
internal struct DWriteLineMetrics
{
    public uint Length;
    public uint TrailingWhitespaceLength;
    public uint NewlineLength;
    public float Height;
    public float Baseline;
    public int IsTrimmed;
}

/// <summary>DWRITE_OVERHANG_METRICS describes visible ink relative to the layout edges.</summary>
internal readonly struct DWriteOverhangMetrics(
    float left,
    float top,
    float right,
    float bottom)
{
    public readonly float Left = left;
    public readonly float Top = top;
    public readonly float Right = right;
    public readonly float Bottom = bottom;
}

/// <summary>DWRITE_TRIMMING.</summary>
internal readonly struct DWriteTrimming(uint granularity, uint delimiter, uint delimiterCount)
{
    public readonly uint Granularity = granularity;
    public readonly uint Delimiter = delimiter;
    public readonly uint DelimiterCount = delimiterCount;
}

internal enum DWriteTextAlignment : uint
{
    Leading = 0,
    Trailing = 1,
    Center = 2,
}

internal enum DWriteParagraphAlignment : uint
{
    Near = 0,
    Center = 2,
}

/// <summary>ID2D1SolidColorBrush.</summary>
internal sealed unsafe class D2DBrush(void* pointer) : ComObject(pointer);

/// <summary>ID2D1Bitmap.</summary>
internal sealed unsafe class D2DBitmap(void* pointer) : ComObject(pointer)
{
    private const int CopyFromBitmapSlot = 8;
    private const int CopyFromMemorySlot = 10;

    /// <summary>Copies a pixel rectangle from a bitmap of the same format to the origin.</summary>
    public void CopyFromBitmap(D2DBitmap source, in D2DRectU region)
    {
        var copy = (delegate* unmanaged<void*, void*, void*, in D2DRectU, int>)Methods[CopyFromBitmapSlot];
        Check(copy(Pointer, null, source.Pointer, in region));
    }

    /// <summary>Replaces every pixel with rows of the bitmap's own format, from the top.</summary>
    public void CopyFromMemory(ReadOnlySpan<byte> pixels, uint stride)
    {
        var copy = (delegate* unmanaged<void*, void*, byte*, uint, int>)Methods[CopyFromMemorySlot];
        fixed (byte* source = pixels) Check(copy(Pointer, null, source, stride));
    }
}

/// <summary>ID2D1PathGeometry.</summary>
internal sealed unsafe class D2DPathGeometry(void* pointer) : ComObject(pointer)
{
    private const int OpenSlot = 17;

    public D2DGeometrySink Open()
    {
        Check(((delegate* unmanaged<void*, out void*, int>)Methods[OpenSlot])(Pointer, out var sink));
        return new D2DGeometrySink(sink);
    }
}

/// <summary>ID2D1GeometrySink, used for closed filled figures of straight lines.</summary>
internal sealed unsafe class D2DGeometrySink(void* pointer) : ComObject(pointer)
{
    private const int BeginFigureSlot = 5;
    private const int AddLinesSlot = 6;
    private const int EndFigureSlot = 8;
    private const int CloseSlot = 9;
    private const uint FigureBeginFilled = 0;
    private const uint FigureEndClosed = 1;

    /// <summary>Adds a filled, closed figure through the points.</summary>
    public void AddPolygon(Vector2 start, ReadOnlySpan<Vector2> points)
    {
        var beginFigure = (delegate* unmanaged<void*, Vector2, uint, void>)Methods[BeginFigureSlot];
        var addLines = (delegate* unmanaged<void*, Vector2*, uint, void>)Methods[AddLinesSlot];
        var endFigure = (delegate* unmanaged<void*, uint, void>)Methods[EndFigureSlot];
        beginFigure(Pointer, start, FigureBeginFilled);
        fixed (Vector2* lines = points) addLines(Pointer, lines, (uint)points.Length);

        endFigure(Pointer, FigureEndClosed);
    }

    public void Close() => Check(((delegate* unmanaged<void*, int>)Methods[CloseSlot])(Pointer));
}

/// <summary>ID2D1Factory1.</summary>
internal sealed unsafe partial class D2DFactory(void* pointer) : ComObject(pointer)
{
    private const int CreatePathGeometrySlot = 10;
    private const int CreateDeviceSlot = 17;
    private const uint SingleThreaded = 0;

    private static readonly Guid Iid = new("bb12d362-daee-4b9a-aa1d-14ba401cfa1f");

    public static D2DFactory Create()
    {
        Check(D2D1CreateFactory(SingleThreaded, in Iid, options: null, out var factory));
        return new D2DFactory(factory);
    }

    public D2DPathGeometry CreatePathGeometry()
    {
        var create = (delegate* unmanaged<void*, out void*, int>)Methods[CreatePathGeometrySlot];
        Check(create(Pointer, out var geometry));
        return new D2DPathGeometry(geometry);
    }

    public D2DDevice CreateDevice(DxgiDevice dxgiDevice)
    {
        var create = (delegate* unmanaged<void*, void*, out void*, int>)Methods[CreateDeviceSlot];
        Check(create(Pointer, dxgiDevice.Pointer, out var device));
        return new D2DDevice(device);
    }

    [LibraryImport("d2d1.dll")]
    private static partial int D2D1CreateFactory(
        uint factoryType,
        in Guid iid,
        void* options,
        out void* factory);
}

/// <summary>ID2D1Device.</summary>
internal sealed unsafe class D2DDevice(void* pointer) : ComObject(pointer)
{
    private const int CreateDeviceContextSlot = 4;

    public D2DDeviceContext CreateDeviceContext()
    {
        var create = (delegate* unmanaged<void*, uint, out void*, int>)Methods[CreateDeviceContextSlot];
        Check(create(Pointer, 0, out var context));
        return new D2DDeviceContext(context);
    }
}

/// <summary>ID2D1DeviceContext, with the ID2D1RenderTarget methods it inherits.</summary>
internal sealed unsafe class D2DDeviceContext(void* pointer) : ComObject(pointer)
{
    private const int CreateSolidColorBrushSlot = 8;
    private const int DrawLineSlot = 15;
    private const int DrawRectangleSlot = 16;
    private const int FillRectangleSlot = 17;
    private const int DrawRoundedRectangleSlot = 18;
    private const int FillRoundedRectangleSlot = 19;
    private const int FillEllipseSlot = 21;
    private const int FillGeometrySlot = 23;
    private const int DrawTextLayoutSlot = 28;
    private const int SetTransformSlot = 30;
    private const int SetTextAntialiasModeSlot = 34;
    private const int PushAxisAlignedClipSlot = 45;
    private const int PopAxisAlignedClipSlot = 46;
    private const int ClearSlot = 47;
    private const int BeginDrawSlot = 48;
    private const int EndDrawSlot = 49;
    private const int CreateBitmapSlot = 57;
    private const int CreateBitmapFromWicBitmapSlot = 58;
    private const int CreateBitmapFromDxgiSurfaceSlot = 62;
    private const int SetTargetSlot = 74;
    private const int DrawBitmapSlot = 85;

    public D2DBrush CreateSolidColorBrush(in D2DColor color)
    {
        var create = (delegate* unmanaged<void*, in D2DColor, void*, out void*, int>)Methods[CreateSolidColorBrushSlot];
        Check(create(
            Pointer,
            in color,
            null,
            out var brush));
        return new D2DBrush(brush);
    }

    /// <summary>An uninitialized bitmap of the size and properties.</summary>
    public D2DBitmap CreateBitmap(D2DSizeU size, in D2DBitmapProperties1 properties)
    {
        var create =
            (delegate* unmanaged<void*, D2DSizeU, void*, uint, in D2DBitmapProperties1, out void*, int>)Methods[
                CreateBitmapSlot];

        Check(create(
            Pointer,
            size,
            null,
            0,
            in properties,
            out var bitmap));
        return new D2DBitmap(bitmap);
    }

    /// <summary>A bitmap from a WIC source, with the source's pixel format at 96 DPI.</summary>
    public D2DBitmap CreateBitmapFromWicBitmap(ComObject source)
    {
        var create = (delegate* unmanaged<void*, void*, void*, out void*, int>)Methods[CreateBitmapFromWicBitmapSlot];
        Check(create(
            Pointer,
            source.Pointer,
            null,
            out var bitmap));
        return new D2DBitmap(bitmap);
    }

    public D2DBitmap CreateBitmapFromDxgiSurface(ComObject surface, in D2DBitmapProperties1 properties)
    {
        var create =
            (delegate* unmanaged<void*, void*, in D2DBitmapProperties1, out void*, int>)Methods[
                CreateBitmapFromDxgiSurfaceSlot];

        Check(create(Pointer, surface.Pointer, in properties, out var bitmap));
        return new D2DBitmap(bitmap);
    }

    /// <summary>Sets the bitmap that drawing targets, or none.</summary>
    public void SetTarget(D2DBitmap? target) =>
        ((delegate* unmanaged<void*, void*, void>)Methods[SetTargetSlot])(Pointer, PointerOf(target));

    public void SetTransform(in Matrix3x2 transform) =>
        ((delegate* unmanaged<void*, in Matrix3x2, void>)Methods[SetTransformSlot])(Pointer, in transform);

    /// <summary>ClearType text antialiasing, which needs opaque targets (alpha mode ignore).</summary>
    public void UseClearTypeTextAntialiasing()
    {
        const uint ClearType = 1;
        ((delegate* unmanaged<void*, uint, void>)Methods[SetTextAntialiasModeSlot])(Pointer, ClearType);
    }

    public void BeginDraw() => ((delegate* unmanaged<void*, void>)Methods[BeginDrawSlot])(Pointer);

    /// <summary>Ends drawing; returns the HRESULT, which reports a lost device.</summary>
    public int EndDraw() => ((delegate* unmanaged<void*, void*, void*, int>)Methods[EndDrawSlot])(Pointer, null, null);

    public void Clear(in D2DColor color) =>
        ((delegate* unmanaged<void*, in D2DColor, void>)Methods[ClearSlot])(Pointer, in color);

    public void FillRectangle(in D2DRect rect, D2DBrush brush) =>
        ((delegate* unmanaged<void*, in D2DRect, void*, void>)Methods[FillRectangleSlot])(
            Pointer,
            in rect,
            brush.Pointer);

    public void DrawRectangle(in D2DRect rect, D2DBrush brush, float strokeWidth)
    {
        var draw = (delegate* unmanaged<void*, in D2DRect, void*, float, void*, void>)Methods[DrawRectangleSlot];
        draw(
            Pointer,
            in rect,
            brush.Pointer,
            strokeWidth,
            null);
    }

    public void DrawRoundedRectangle(in D2DRoundedRect rect, D2DBrush brush, float strokeWidth)
    {
        var draw = (delegate* unmanaged<void*, in D2DRoundedRect, void*, float, void*, void>)Methods[DrawRoundedRectangleSlot];
        draw(
            Pointer,
            in rect,
            brush.Pointer,
            strokeWidth,
            null);
    }

    public void FillRoundedRectangle(in D2DRoundedRect rect, D2DBrush brush)
    {
        var fill = (delegate* unmanaged<void*, in D2DRoundedRect, void*, void>)Methods[FillRoundedRectangleSlot];
        fill(Pointer, in rect, brush.Pointer);
    }

    public void DrawLine(
        Vector2 from,
        Vector2 to,
        D2DBrush brush,
        float strokeWidth)
    {
        var draw = (delegate* unmanaged<void*, Vector2, Vector2, void*, float, void*, void>)Methods[DrawLineSlot];
        draw(
            Pointer,
            from,
            to,
            brush.Pointer,
            strokeWidth,
            null);
    }

    public void FillEllipse(in D2DEllipse ellipse, D2DBrush brush) =>
        ((delegate* unmanaged<void*, in D2DEllipse, void*, void>)Methods[FillEllipseSlot])(
            Pointer,
            in ellipse,
            brush.Pointer);

    public void FillGeometry(D2DPathGeometry geometry, D2DBrush brush) =>
        ((delegate* unmanaged<void*, void*, void*, void*, void>)Methods[FillGeometrySlot])(
            Pointer,
            geometry.Pointer,
            brush.Pointer,
            null);

    /// <summary>Draws the whole bitmap into the rectangle with linear interpolation.</summary>
    public void DrawBitmap(D2DBitmap bitmap, in D2DRect destination, float opacity)
    {
        const uint Linear = 1;
        var draw =
            (delegate* unmanaged<void*, void*, in D2DRect, float, uint, void*, void*, void>)Methods[DrawBitmapSlot];

        draw(
            Pointer,
            bitmap.Pointer,
            in destination,
            opacity,
            Linear,
            null,
            null);
    }

    public void DrawTextLayout(
        Vector2 origin,
        DWriteTextLayout layout,
        D2DBrush brush,
        bool isClipped)
    {
        const uint Clip = 2;
        var draw = (delegate* unmanaged<void*, Vector2, void*, void*, uint, void>)Methods[DrawTextLayoutSlot];
        draw(
            Pointer,
            origin,
            layout.Pointer,
            brush.Pointer,
            isClipped ? Clip : 0);
    }

    /// <summary>Clips to the rectangle without antialiasing until <see cref="PopAxisAlignedClip"/>.</summary>
    public void PushAxisAlignedClip(in D2DRect rect)
    {
        const uint Aliased = 1;
        var push = (delegate* unmanaged<void*, in D2DRect, uint, void>)Methods[PushAxisAlignedClipSlot];
        push(Pointer, in rect, Aliased);
    }

    public void PopAxisAlignedClip() => ((delegate* unmanaged<void*, void>)Methods[PopAxisAlignedClipSlot])(Pointer);
}

/// <summary>IDWriteFactory.</summary>
internal sealed unsafe partial class DWriteFactory(void* pointer) : ComObject(pointer)
{
    private const int GetSystemFontCollectionSlot = 3;
    private const int CreateTextFormatSlot = 15;
    private const int CreateTextLayoutSlot = 18;
    private const int CreateEllipsisTrimmingSignSlot = 20;
    private const uint Shared = 0;

    private static readonly Guid Iid = new("b859ee5a-d838-4b5b-a2e8-1adc7d93db48");

    public static DWriteFactory Create()
    {
        Check(DWriteCreateFactory(Shared, in Iid, out var factory));
        return new DWriteFactory(factory);
    }

    public bool HasFontFamily(string familyName)
    {
        var getCollection = (delegate* unmanaged<void*, out void*, int, int>)Methods[GetSystemFontCollectionSlot];
        Check(getCollection(Pointer, out var pointer, 0));

        using var collection = new ComObject(pointer);
        const int FindFamilyNameSlot = 5;
        var find = (delegate* unmanaged<void*, char*, out uint, out int, int>)(*(void***)collection.Pointer)[FindFamilyNameSlot];
        fixed (char* family = familyName)
        {
            Check(find(collection.Pointer, family, out _, out var exists));
            return exists != 0;
        }
    }

    /// <summary>A text format of the system font family with normal style and stretch.</summary>
    public DWriteTextFormat CreateTextFormat(string familyName, int weight, float size)
    {
        const int NormalStyle = 0;
        const int NormalStretch = 5;
        var create =
            (delegate* unmanaged<void*, char*, void*, int, int, int, float, char*, out void*, int>)Methods[
                CreateTextFormatSlot];

        fixed (char* family = familyName)
        fixed (char* locale = "")
        {
            Check(create(
                Pointer,
                family,
                null,
                weight,
                NormalStyle,
                NormalStretch,
                size,
                locale,
                out var format));

            return new DWriteTextFormat(format);
        }
    }

    public DWriteTextLayout CreateTextLayout(
        string text,
        DWriteTextFormat format,
        float maximumWidth,
        float maximumHeight)
    {
        var create =
            (delegate* unmanaged<void*, char*, uint, void*, float, float, out void*, int>)Methods[CreateTextLayoutSlot];

        fixed (char* characters = text)
        {
            Check(
                create(
                    Pointer,
                    characters,
                    (uint)text.Length,
                    format.Pointer,
                    maximumWidth,
                    maximumHeight,
                    out var layout));

            return new DWriteTextLayout(layout);
        }
    }

    public ComObject CreateEllipsisTrimmingSign(DWriteTextFormat format)
    {
        var create = (delegate* unmanaged<void*, void*, out void*, int>)Methods[CreateEllipsisTrimmingSignSlot];
        Check(create(Pointer, format.Pointer, out var sign));
        return new ComObject(sign);
    }

    [LibraryImport("dwrite.dll")]
    private static partial int DWriteCreateFactory(uint factoryType, in Guid iid, out void* factory);
}

/// <summary>IDWriteTextFormat.</summary>
internal sealed unsafe class DWriteTextFormat(void* pointer) : ComObject(pointer)
{
    private const int SetTextAlignmentSlot = 3;
    private const int SetParagraphAlignmentSlot = 4;
    private const int SetWordWrappingSlot = 5;
    private const int SetTrimmingSlot = 9;

    public void SetTextAlignment(DWriteTextAlignment alignment) =>
        Check(
            ((delegate* unmanaged<void*, DWriteTextAlignment, int>)Methods[SetTextAlignmentSlot])(Pointer, alignment));

    public void SetParagraphAlignment(DWriteParagraphAlignment alignment)
    {
        var set = (delegate* unmanaged<void*, DWriteParagraphAlignment, int>)Methods[SetParagraphAlignmentSlot];
        Check(set(Pointer, alignment));
    }

    public void SetWordWrapping(bool isWrapped)
    {
        const uint Wrap = 0;
        const uint NoWrap = 1;
        var set = (delegate* unmanaged<void*, uint, int>)Methods[SetWordWrappingSlot];
        Check(set(Pointer, isWrapped ? Wrap : NoWrap));
    }

    /// <summary>Trims text that overflows the layout at a character, showing the given sign.</summary>
    public void SetCharacterTrimming(ComObject sign)
    {
        const uint CharacterGranularity = 1;
        var trimming = new DWriteTrimming(granularity: CharacterGranularity, delimiter: 0, delimiterCount: 0);
        var set = (delegate* unmanaged<void*, in DWriteTrimming, void*, int>)Methods[SetTrimmingSlot];
        Check(set(Pointer, in trimming, sign.Pointer));
    }
}

/// <summary>IDWriteTextLayout.</summary>
internal sealed unsafe class DWriteTextLayout(void* pointer) : ComObject(pointer)
{
    private const int GetLineMetricsSlot = 59;
    private const int GetMetricsSlot = 60;
    private const int GetOverhangMetricsSlot = 61;

    private float? firstTwoLinesHeight;

    public float GetFirstTwoLinesHeight() => firstTwoLinesHeight ??= MeasureFirstTwoLines();

    private float MeasureFirstTwoLines()
    {
        var metrics = GetMetrics();
        if (metrics.LineCount <= 2) return metrics.Height;

        const int StackLimit = 64;
        var lineCount = checked((int)metrics.LineCount);
        Span<DWriteLineMetrics> lines = lineCount <= StackLimit ? stackalloc DWriteLineMetrics[lineCount] : new DWriteLineMetrics[lineCount];
        var get = (delegate* unmanaged<void*, DWriteLineMetrics*, uint, out uint, int>)Methods[GetLineMetricsSlot];
        fixed (DWriteLineMetrics* buffer = lines) Check(get(Pointer, buffer, metrics.LineCount, out _));

        return lines[0].Height + lines[1].Height;
    }

    public DWriteTextMetrics GetMetrics()
    {
        var get = (delegate* unmanaged<void*, out DWriteTextMetrics, int>)Methods[GetMetricsSlot];
        Check(get(Pointer, out var metrics));
        return metrics;
    }

    public DWriteOverhangMetrics GetOverhangMetrics()
    {
        var get = (delegate* unmanaged<void*, out DWriteOverhangMetrics, int>)Methods[GetOverhangMetricsSlot];
        Check(get(Pointer, out var metrics));
        return metrics;
    }
}
