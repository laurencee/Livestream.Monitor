using Livestream.Monitor.Core;
using Livestream.Monitor.Direct2D.Native;

namespace Livestream.Monitor.Direct2D.Rendering;

/// <summary>
/// Direct2D bitmaps of the embedded images (live state icons and country flags), kept while the cache lives. They
/// belong to the shared device and are recreated after it is recreated.
/// </summary>
internal sealed class ImageCache(GraphicsDevice graphics) : IDisposable
{
    private static readonly Dictionary<string, string> CountryMappings = EmbeddedImages.ReadCountryMappings();
    private readonly Dictionary<string, EmbeddedImage> embedded = [];
    private readonly Dictionary<string, string> flagNames = [];

    /// <summary>The flag of a broadcaster language or country code, or null when there is none.</summary>
    public D2DBitmap? GetFlag(string? language)
    {
        if (string.IsNullOrWhiteSpace(language)) return null;

        if (!flagNames.TryGetValue(language, out var name))
        {
            var country = CountryMappings.GetValueOrDefault(language, language);
            name = EmbeddedImages.GetFlagName(country);
            flagNames.Add(language, name);
        }

        return GetEmbedded(name);
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
        foreach (var image in embedded.Values) image.Bitmap?.Dispose();

        embedded.Clear();
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
