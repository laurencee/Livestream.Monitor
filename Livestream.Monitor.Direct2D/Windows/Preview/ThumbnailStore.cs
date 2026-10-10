namespace Livestream.Monitor.Direct2D.Windows.Preview;

/// <summary>
/// Downloads thumbnails and keeps their encoded bytes, most recently used first, within a fixed byte budget.
/// Encoded thumbnails are a few tens of kilobytes; decoding happens only for the image being shown.
/// </summary>
internal sealed class ThumbnailStore(Action loaded) : IDisposable
{
    private const int MaximumThumbnailBytes = 4 * 1024 * 1024;
    private const long MaximumCachedBytes = 8 * 1024 * 1024;
    private const int MaximumConcurrentDownloads = 4;
    private const long RetryMilliseconds = 30_000;
    private readonly HttpClient client = new() { MaxResponseContentBufferSize = MaximumThumbnailBytes };
    private readonly SemaphoreSlim downloads = new(MaximumConcurrentDownloads);
    private readonly CancellationTokenSource lifetime = new();
    private readonly Dictionary<string, Thumbnail> thumbnails = [];
    private long cachedBytes;
    private bool disposed;

    /// <summary>The encoded image, or null while it downloads or after a failed download (retried later).</summary>
    public byte[]? Get(string url)
    {
        var downloadable = Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                           (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

        if (!downloadable) return null;

        var now = Environment.TickCount64;

        if (!thumbnails.TryGetValue(url, out var thumbnail))
        {
            thumbnail = new Thumbnail();
            thumbnails.Add(url, thumbnail);
        }

        thumbnail.LastUse = now;
        if (thumbnail.Encoded != null || thumbnail.Downloading || now < thumbnail.RetryAt) return thumbnail.Encoded;

        thumbnail.Downloading = true;
        _ = Download(url, thumbnail);
        return null;
    }

    public void Dispose()
    {
        disposed = true;
        lifetime.Cancel();
        client.Dispose();
        thumbnails.Clear();
        lifetime.Dispose();
        downloads.Dispose();
    }

    private async Task Download(string url, Thumbnail thumbnail)
    {
        var token = lifetime.Token;
        try
        {
            await downloads.WaitAsync(token);
            try
            {
                var encoded = await client.GetByteArrayAsync(url, token);

                // The continuation runs on the UI thread, which owns the dictionary.
                if (disposed || !thumbnails.TryGetValue(url, out var current) || current != thumbnail) return;

                thumbnail.Encoded = encoded;
                cachedBytes += encoded.Length;
                EvictOverBudget(url);
                loaded();
            }
            finally
            {
                if (!disposed) downloads.Release();
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            // Unavailable remote thumbnails leave the preview empty; the stream's text and actions stay usable.
            thumbnail.RetryAt = Environment.TickCount64 + RetryMilliseconds;
        }
        finally
        {
            thumbnail.Downloading = false;
        }
    }

    // Releases the least recently used downloaded thumbnails, never the one just added, until within the budget.
    private void EvictOverBudget(string keep)
    {
        while (cachedBytes > MaximumCachedBytes)
        {
            var oldest = thumbnails
                .Where(pair => pair.Value.Encoded != null && pair.Key != keep)
                .MinBy(pair => pair.Value.LastUse);

            if (oldest.Key == null) return;

            cachedBytes -= oldest.Value.Encoded!.Length;
            thumbnails.Remove(oldest.Key);
        }
    }

    private sealed class Thumbnail
    {
        public byte[]? Encoded;
        public bool Downloading;
        public long RetryAt;
        public long LastUse;
    }
}
