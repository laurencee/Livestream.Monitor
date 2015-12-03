using System.Collections.Generic;
using System.Linq;
using Livestream.Monitor.Core;

namespace Livestream.Monitor.Model
{
    // TODO consider replacing this with a container class that gets given a collection stream providers at run time
    public static class StreamProviders
    {
        public const string TWITCH_STREAM_PROVIDER = "twitchtv";
        public const string YOUTUBE_STREAM_PROVIDER = "youtube";

        public static readonly List<string> ValidProviders = new List<string>()
        {
            TWITCH_STREAM_PROVIDER,
            YOUTUBE_STREAM_PROVIDER
        };

        public static bool IsValidProvider(string providerName)
        {
            return ValidProviders.Any(x => x.IsEqualTo(providerName));
        }
    }
}