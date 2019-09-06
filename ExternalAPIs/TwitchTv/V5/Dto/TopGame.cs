using Newtonsoft.Json;

namespace ExternalAPIs.TwitchTv.V5.Dto
{
    public class TopGame
    {
        [JsonProperty("channels")]
        public long Channels { get; set; }

        [JsonProperty("viewers")]
        public long Viewers { get; set; }

        [JsonProperty("game")]
        public Game Game { get; set; }
    }
}