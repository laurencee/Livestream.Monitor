using System.Runtime.InteropServices;
using Livestream.Monitor.Direct2D.Native;

namespace Livestream.Monitor.Direct2D.Rendering;

/// <summary>
/// One Direct2D bitmap whose pixels are replaced in place by decoded images, scaled to its fixed pixel size. The
/// bitmap and its CPU copy are allocated once per size, so showing further images allocates no image memory;
/// the CPU copy also restores the bitmap after the shared device is recreated.
/// </summary>
internal sealed class ReusableBitmap(GraphicsDevice graphics) : IDisposable
{
    private const int BytesPerPixel = 4;
    private byte[] pixels = [];
    private D2DBitmap? bitmap;
    private int bitmapGeneration;
    private bool hasImage;

    public int PixelWidth { get; private set; }
    public int PixelHeight { get; private set; }

    /// <summary>Changes whenever the displayed pixels change; draw commands compare it to detect new content.</summary>
    public int Revision { get; private set; }

    /// <summary>The bitmap holding the last loaded image, or null when there is none.</summary>
    public D2DBitmap? Bitmap
    {
        get
        {
            if (!hasImage) return null;
            if (bitmap != null && bitmapGeneration == graphics.Generation) return bitmap;

            bitmap?.Dispose();
            bitmap = CreateBitmap();
            Upload();
            return bitmap;
        }
    }

    /// <summary>Decodes an image scaled to the size; shows nothing for invalid data.</summary>
    public unsafe void Load(byte[] encoded, int width, int height)
    {
        Resize(width, height);
        hasImage = false;
        Revision++;
        try
        {
            fixed (byte* data = encoded)
            {
                var factory = graphics.ImagingFactory;
                using var stream = factory.CreateMemoryStream(data, encoded.Length);
                using var converted = factory.DecodeFirstFrame(stream, WicFactory.PixelFormat32BppPbgra);
                using var scaled = factory.CreateScaler(converted, PixelWidth, PixelHeight);
                scaled.CopyPixels(Stride, pixels);
            }
        }
        catch (COMException)
        {
            return;
        }

        hasImage = true;
        if (bitmap != null && bitmapGeneration == graphics.Generation) Upload();
    }

    /// <summary>Shows nothing until the next image loads.</summary>
    public void Clear()
    {
        if (!hasImage) return;

        hasImage = false;
        Revision++;
    }

    public void Dispose()
    {
        bitmap?.Dispose();
        bitmap = null;
    }

    private uint Stride => (uint)(PixelWidth * BytesPerPixel);

    private void Resize(int width, int height)
    {
        width = Math.Max(width, 1);
        height = Math.Max(height, 1);
        if (width == PixelWidth && height == PixelHeight) return;

        PixelWidth = width;
        PixelHeight = height;
        pixels = GC.AllocateUninitializedArray<byte>(width * height * BytesPerPixel);
        bitmap?.Dispose();
        bitmap = null;
    }

    private D2DBitmap CreateBitmap()
    {
        const uint PremultipliedAlpha = 1;
        const uint NoOptions = 0;
        var properties = new D2DBitmapProperties1(
            pixelFormat: new D2DPixelFormat(DxgiFormat.B8G8R8A8UNorm, PremultipliedAlpha),
            dpiX: 96,
            dpiY: 96,
            options: NoOptions,
            colorContext: 0);

        var size = new D2DSizeU((uint)PixelWidth, (uint)PixelHeight);
        var created = graphics.Context.CreateBitmap(size, properties);
        bitmapGeneration = graphics.Generation;
        return created;
    }

    private void Upload() => bitmap!.CopyFromMemory(pixels, Stride);
}
