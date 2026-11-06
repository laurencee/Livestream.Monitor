using Livestream.Monitor.Core;
using Livestream.Monitor.Direct2D.Native;

namespace Livestream.Monitor.Direct2D.Rendering;

/// <summary>
/// Direct2D bitmaps of the embedded images (live state icons and country flags), kept while the cache lives. They
/// belong to the shared device and are recreated after it is recreated.
/// </summary>
internal sealed class ImageCache(GraphicsDevice graphics) : IDisposable
{
    private readonly Dictionary<string, EmbeddedImage> embedded = [];
    private readonly Dictionary<FlagRegion, D2DBitmap> flags = [];
    private int flagGeneration;

    /// <summary>The flag of a broadcaster language or country code, or null when there is none.</summary>
    public D2DBitmap? GetFlag(string? language)
    {
        if (EmbeddedImages.FindFlag(language) is not { } region) return null;
        if (region.IsStandalone) return GetEmbedded(EmbeddedImages.GuernseyFlag);

        if (flagGeneration != graphics.Generation)
        {
            ClearFlags();
            flagGeneration = graphics.Generation;
        }

        if (flags.TryGetValue(region, out var bitmap)) return bitmap;

        var atlas = GetEmbedded(EmbeddedImages.FlagAtlas) ?? throw new InvalidOperationException("Missing flag atlas");
        bitmap = CopyFlag(atlas, region);
        flags.Add(region, bitmap);
        return bitmap;
    }

    /// <summary>An <see cref="EmbeddedImages"/> image, or null when there is no such image.</summary>
    public D2DBitmap? GetEmbedded(string name)
    {
        if (embedded.TryGetValue(name, out var image) && image.Generation == graphics.Generation) return image.Bitmap;

        image.Bitmap?.Dispose();
        var bitmap = Load(name);
        embedded[name] = new EmbeddedImage(bitmap, graphics.Generation);
        return bitmap;
    }

    public void Dispose()
    {
        ClearFlags();

        foreach (var image in embedded.Values) image.Bitmap?.Dispose();

        embedded.Clear();
    }

    private void ClearFlags()
    {
        foreach (var flag in flags.Values) flag.Dispose();

        flags.Clear();
    }

    private D2DBitmap CopyFlag(D2DBitmap atlas, FlagRegion region)
    {
        const uint PremultipliedAlpha = 1;
        var properties = new D2DBitmapProperties1(
            pixelFormat: new D2DPixelFormat(DxgiFormat.B8G8R8A8UNorm, PremultipliedAlpha),
            dpiX: 96,
            dpiY: 96,
            options: 0,
            colorContext: 0);

        var size = new D2DSizeU((uint)region.Width, (uint)region.Height);
        var bounds = new D2DRectU(
            left: (uint)region.X,
            top: (uint)region.Y,
            right: (uint)(region.X + region.Width),
            bottom: (uint)(region.Y + region.Height));

        var bitmap = graphics.Context.CreateBitmap(size, properties);
        try
        {
            bitmap.CopyFromBitmap(atlas, bounds);
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    private unsafe D2DBitmap? Load(string name)
    {
        using var resource = EmbeddedImages.Open(name);
        if (resource == null) return null;

        // Resources of an assembly loaded from a file are mapped memory, which decodes without a copy.
        if (resource is UnmanagedMemoryStream mapped)
            return Decode(mapped.PositionPointer, checked((int)mapped.Length));

        using var memory = new MemoryStream();
        resource.CopyTo(memory);
        var bytes = memory.ToArray();
        fixed (byte* data = bytes) return Decode(data, bytes.Length);
    }

    private unsafe D2DBitmap Decode(byte* data, int length)
    {
        using var stream = graphics.ImagingFactory.CreateMemoryStream(data, length);
        using var pixels = graphics.ImagingFactory.DecodeFirstFrame(stream, WicFactory.PixelFormat32BppPbgra);
        return graphics.Context.CreateBitmapFromWicBitmap(pixels);
    }

    private readonly record struct EmbeddedImage(D2DBitmap? Bitmap, int Generation);
}
