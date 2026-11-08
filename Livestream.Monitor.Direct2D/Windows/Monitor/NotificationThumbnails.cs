using System.Runtime.InteropServices;
using Livestream.Monitor.Direct2D.Native;

namespace Livestream.Monitor.Direct2D.Windows.Monitor;

/// <summary>Keeps local copies of the preview images used by Windows notification history.</summary>
internal sealed class NotificationThumbnails : IDisposable
{
    private const int MaximumImageBytes = 512 * 1024;
    private const int MaximumCachedImages = 64;
    private const int MaximumDownloads = 4;
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);
    private static ReadOnlySpan<byte> JpegSignature => [0xff, 0xd8, 0xff];
    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
    private readonly HttpClient client = new()
    {
        MaxResponseContentBufferSize = MaximumImageBytes,
        Timeout = TimeSpan.FromSeconds(2),
    };
    private int activeDownloads;

    public async Task<Uri?> Find(string? url, bool channelIcon = false)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) return null;

        var active = Interlocked.Increment(ref activeDownloads);
        try
        {
            if (active > MaximumDownloads) return null;

            var bytes = await client.GetByteArrayAsync(uri).ConfigureAwait(false);
            var extension = GetExtension(bytes);
            var isWebP = IsWebP(bytes);
            if (extension == null && !isWebP) return null;

            var convertToPng = channelIcon || isWebP;
            var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(localData)) return null;

            var folder = Path.Combine(localData, "Livestream.Monitor", "Direct2D", "NotificationThumbnails");
            var directory = Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, Guid.NewGuid().ToString("N") + (convertToPng ? ".png" : extension));
            if (convertToPng) SaveAsPng(bytes, path);
            else await File.WriteAllBytesAsync(path, bytes).ConfigureAwait(false);

            Prune(directory, path);

            return new Uri(path, UriKind.Absolute);
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or IOException or
                                          UnauthorizedAccessException or COMException or ObjectDisposedException)
        {
            return null;
        }
        finally
        {
            Interlocked.Decrement(ref activeDownloads);
        }
    }

    public void Dispose() => client.Dispose();

    private static string? GetExtension(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(JpegSignature)) return ".jpg";
        if (bytes.StartsWith(PngSignature)) return ".png";

        return null;
    }

    private static bool IsWebP(ReadOnlySpan<byte> bytes)
    {
        const int WebPMarkerOffset = 8;
        return bytes.StartsWith("RIFF"u8) && bytes.Length >= WebPMarkerOffset && bytes[WebPMarkerOffset..].StartsWith("WEBP"u8);
    }

    // Decode icons before choosing them over the thumbnail. Kick may also return WebP, which needs conversion for a toast.
    private static unsafe void SaveAsPng(byte[] bytes, string path)
    {
        const uint MultiThreaded = 0;
        var initialization = Win32.CoInitializeEx(0, MultiThreaded);
        const int ChangedMode = unchecked((int)0x80010106);
        if (initialization != ChangedMode) ComObject.Check(initialization);

        try
        {
            using var factory = WicFactory.Create();
            fixed (byte* data = bytes)
            {
                using var source = factory.CreateMemoryStream(data, bytes.Length);
                using var pixels = factory.DecodeFirstFrame(source, WicFactory.PixelFormat32BppBgra);
                const int IconSize = 128;
                const int BytesPerPixel = 4;
                const uint Stride = IconSize * BytesPerPixel;
                using var scaler = factory.CreateScaler(pixels, IconSize, IconSize);
                var buffer = new byte[IconSize * Stride];
                scaler.CopyPixels(Stride, buffer);

                var pixelFormat = WicFactory.PixelFormat32BppBgra;
                using var output = factory.CreateFileStream(path);
                fixed (byte* scaled = buffer)
                    factory.EncodePng(output, IconSize, IconSize, Stride, scaled, pixelFormat);
            }
        }
        finally
        {
            if (initialization >= 0) Win32.CoUninitialize();
        }
    }

    private static void Prune(DirectoryInfo directory, string currentPath)
    {
        try
        {
            var cutoff = DateTime.UtcNow - Retention;
            var previous = directory.EnumerateFiles()
                .Where(file => file.FullName != currentPath && file.Extension is ".jpg" or ".png" &&
                               Guid.TryParseExact(Path.GetFileNameWithoutExtension(file.Name), "N", out _))
                .OrderByDescending(file => file.LastWriteTimeUtc);

            var retained = 1;
            foreach (var file in previous)
            {
                if (file.LastWriteTimeUtc < cutoff || ++retained > MaximumCachedImages) file.Delete();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A cache cleanup failure must not hide an otherwise available thumbnail.
        }
    }
}
