using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Livestream.Monitor.Core
{
    /// <summary>
    /// Data kept between sessions only to save requests or time; a missing or unreadable cache starts empty and costs
    /// only the requests it would have saved.
    /// </summary>
    public interface ICacheHandler
    {
        /// <summary> The cached data, loaded on first use. </summary>
        Cache Cache { get; }

        /// <summary>
        /// Replaces the cached data with the result of <paramref name="update"/> and saves it; updates from different threads
        /// apply one at a time. Returning the given instance saves nothing.
        /// </summary>
        void Update(Func<Cache, Cache> update);
    }

    /// <param name="KickAppAccessToken"> Kick's app access token, reused across sessions until shortly before it expires. </param>
    /// <param name="YouTubeLivestreamVideoIds">
    /// Each YouTube channel's livestream playlist video IDs from its last complete check, keyed by channel ID; looked up
    /// while the next check's playlist request is in flight.
    /// </param>
    public sealed record Cache(
        KickAppAccessToken KickAppAccessToken,
        IReadOnlyDictionary<string, IReadOnlyList<string>> YouTubeLivestreamVideoIds)
    {
        public static Cache Empty { get; } = new(KickAppAccessToken: null, new Dictionary<string, IReadOnlyList<string>>());
    }

    public sealed record KickAppAccessToken(string Token, DateTimeOffset ExpiresAt);

    public class CacheHandler(string filePath = CacheHandler.CacheFileName) : ICacheHandler
    {
        private const string CacheFileName = "cache.bin";

        // Raised whenever the layout changes; a cache written in another layout is ignored.
        private const int FormatVersion = 1;
        // Bounds the counts read from the file, so a damaged one cannot request huge allocations.
        private const int MaxEntries = 100_000;

        // Kick's token and YouTube's video IDs are saved from background query threads.
        private readonly Lock updateLock = new();
        private Cache cache;

        public Cache Cache
        {
            get
            {
                lock (updateLock) return cache ??= Load();
            }
        }

        public void Update(Func<Cache, Cache> update)
        {
            ArgumentNullException.ThrowIfNull(update);

            lock (updateLock)
            {
                var current = cache ??= Load();
                var updated = update(current);
                if (ReferenceEquals(updated, current)) return;

                cache = updated;
                Save(updated);
            }
        }

        private Cache Load()
        {
            if (!File.Exists(filePath)) return Cache.Empty;

            try
            {
                using var reader = new BinaryReader(File.OpenRead(filePath));
                if (reader.ReadInt32() != FormatVersion) return Cache.Empty;

                KickAppAccessToken kickToken = null;

                if (reader.ReadBoolean())
                {
                    var token = reader.ReadString();
                    kickToken = new KickAppAccessToken(token, new DateTimeOffset(reader.ReadInt64(), TimeSpan.Zero));
                }

                var channelCount = ReadCount(reader);
                var videoIds = new Dictionary<string, IReadOnlyList<string>>(channelCount);

                for (var i = 0; i < channelCount; i++)
                {
                    var channelId = reader.ReadString();
                    var ids = new string[ReadCount(reader)];
                    for (var j = 0; j < ids.Length; j++) ids[j] = reader.ReadString();

                    videoIds[channelId] = ids;
                }

                return new Cache(kickToken, videoIds);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or FormatException or
                                       ArgumentOutOfRangeException)
            {
                return Cache.Empty;
            }

            static int ReadCount(BinaryReader reader)
            {
                var count = reader.ReadInt32();
                if (count is < 0 or > MaxEntries) throw new InvalidDataException($"Invalid cache entry count {count}.");

                return count;
            }
        }

        private void Save(Cache data)
        {
            try
            {
                using var writer = new BinaryWriter(File.Create(filePath));
                writer.Write(FormatVersion);

                writer.Write(data.KickAppAccessToken != null);

                if (data.KickAppAccessToken is { } kickToken)
                {
                    writer.Write(kickToken.Token);
                    writer.Write(kickToken.ExpiresAt.UtcTicks);
                }

                writer.Write(data.YouTubeLivestreamVideoIds.Count);

                foreach (var (channelId, ids) in data.YouTubeLivestreamVideoIds)
                {
                    writer.Write(channelId);
                    writer.Write(ids.Count);
                    foreach (var id in ids) writer.Write(id);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // The session keeps the data in memory; the next session only repeats the requests it would have saved.
            }
        }
    }
}
