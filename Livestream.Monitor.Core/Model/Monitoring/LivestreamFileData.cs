using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Livestream.Monitor.Model.Monitoring
{
    public class LivestreamFileData
    {
        public const int CurrentFileVersion = 2;

        public int FileVersion { get; set; }

        public List<LivestreamFileEntry> LivestreamFileEntries { get; set; }
    }

    public class LivestreamFileEntry
    {
        /// <summary> Identifies the channel, not the stream, because YouTube lets one channel run several livestreams at once. </summary>
        [JsonRequired]
        [JsonPropertyName("LivestreamId")]
        public string ChannelId { get; set; }

        /// <summary> The username this livestream was imported from </summary>
        public string ImportedBy { get; set; }

        public string DisplayName { get; set; }

        public string TwitchLogin { get; set; }

        /// <summary> The site which this livestream belongs to (twitch/youtube etc.) </summary>
        [JsonRequired]
        public string StreamProvider { get; set; }
    }
}
