using System;
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
        
        // TODO - move this into an IStreamProvider interface when youtube livestream support is added
        /// <summary> Returns the base url for the stream (always ending with a forward slash '/') </summary>
        /// <param name="providerName">A provider name constant from <see cref="StreamProviders"/></param>
        public static string GetBaseUrl(string providerName)
        {
            switch (providerName)
            {
                case TWITCH_STREAM_PROVIDER:
                    return @"http://www.twitch.tv/";
                case YOUTUBE_STREAM_PROVIDER:
                    return @"https://www.youtube.com/";
                default:
                    throw new ArgumentException("Unknown provider name " + providerName);
            }
        }

        public static bool IsValidProvider(string providerName)
        {
            return ValidProviders.Any(x => x.IsEqualTo(providerName));
        }
    }
}