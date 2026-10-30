using System;
using System.Collections.Generic;
using System.IO;
using Livestream.Monitor.Core;
using Xunit;

namespace Livestream.Monitor.Tests;

public sealed class CacheHandlerShould : IDisposable
{
    private readonly string cachePath = Path.Combine(Path.GetTempPath(), $"Livestream.Monitor.Cache-{Guid.NewGuid():N}.bin");

    public void Dispose() => File.Delete(cachePath);

    [Fact]
    public void KeepTheCachedDataForTheNextSession()
    {
        var expiresAt = new DateTimeOffset(2026, 12, 30, 1, 2, 3, TimeSpan.Zero);
        var videoIds = new Dictionary<string, IReadOnlyList<string>> { ["UC1"] = ["a", "b"], ["UC2"] = [] };

        new CacheHandler(cachePath).Update(_ => new Cache(new KickAppAccessToken("token", expiresAt), videoIds));
        var cache = new CacheHandler(cachePath).Cache;

        Assert.Equal(new KickAppAccessToken("token", expiresAt), cache.KickAppAccessToken);
        Assert.Equal(["a", "b"], cache.YouTubeLivestreamVideoIds["UC1"]);
        Assert.Empty(cache.YouTubeLivestreamVideoIds["UC2"]);
    }

    [Fact]
    public void StartEmptyFromAnUnreadableCacheAndReplaceIt()
    {
        File.WriteAllBytes(cachePath, [1, 0, 0, 0, 1, 200]);
        var handler = new CacheHandler(cachePath);

        Assert.Null(handler.Cache.KickAppAccessToken);
        Assert.Empty(handler.Cache.YouTubeLivestreamVideoIds);

        handler.Update(cache => cache with { KickAppAccessToken = new KickAppAccessToken("token", DateTimeOffset.UnixEpoch) });

        Assert.Equal("token", new CacheHandler(cachePath).Cache.KickAppAccessToken.Token);
    }
}
