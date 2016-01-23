using Newtonsoft.Json;

namespace Livestream.Monitor.Model.Monitoring
{
    public class LivestreamFileData
    {
        [JsonRequired]
        public string LivestreamId { get; set; }

        /// <summary> The username this livestream was imported from </summary>
        public string ImportedBy { get; set; }

        /// <summary> The site which this livestream belongs to (twitch/youtube etc.) </summary>
        [JsonRequired]
        public string StreamProvider { get; set; }
    }
}
