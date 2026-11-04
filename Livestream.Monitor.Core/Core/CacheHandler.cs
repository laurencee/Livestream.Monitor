using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Livestream.Monitor.Core
{
    /// <summary> Caches data between sessions to save requests or time. Missing or unreadable caches start empty. </summary>
    public interface ICacheHandler
    {
        /// <summary> The cached data, loaded on first use. </summary>
        Cache Cache { get; }

        /// <summary> Replaces the cache with the result of <paramref name="update"/> and saves it, one update at a time. Returning the same instance skips saving. </summary>
        void Update(Func<Cache, Cache> update);
    }

    /// <param name="KickAppAccessToken"> Kick token reused between sessions until near expiry. </param>
    /// <param name="YouTubeLivestreamVideoIds"> Video IDs from each channel's last complete playlist check, keyed by channel ID. </param>
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

        // Increment when the cache format changes; other versions are ignored.
        private const int FormatVersion = 1;
        // Cap counts read from the file so a damaged cache can't cause huge allocations.
        private const int MaxEntries = 100_000;

        // Serialize background updates so one provider doesn't overwrite another's cache changes.
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
                // Keep using the in-memory cache if saving fails.
            }
        }
    }
}
