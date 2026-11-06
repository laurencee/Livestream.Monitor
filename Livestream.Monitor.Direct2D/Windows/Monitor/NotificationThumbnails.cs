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

    public async Task<Uri?> Get(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) return null;

        var active = Interlocked.Increment(ref activeDownloads);
        try
        {
            if (active > MaximumDownloads) return null;

            var bytes = await client.GetByteArrayAsync(uri).ConfigureAwait(false);
            var extension = GetExtension(bytes);
            if (extension == null) return null;

            var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(localData)) return null;

            var folder = Path.Combine(localData, "Livestream.Monitor", "Direct2D", "NotificationThumbnails");
            var directory = Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, Guid.NewGuid().ToString("N") + extension);
            await File.WriteAllBytesAsync(path, bytes).ConfigureAwait(false);
            Prune(directory, path);

            return new Uri(path, UriKind.Absolute);
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or IOException or UnauthorizedAccessException)
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
