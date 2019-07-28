using Newtonsoft.Json;

namespace ExternalAPIs.TwitchTv.V3.Dto
{
    public class ChannelLinks
    {
        public string Self { get; set; }

        public string Follows { get; set; }

        public string Commercial { get; set; }

        [JsonProperty("stream_key")]
        public string StreamKey { get; set; }

        public string Chat { get; set; }

        public string Features { get; set; }

        public string Subscriptions { get; set; }

        public string Editors { get; set; }

        public string Videos { get; set; }

        public string Teams { get; set; }
    }
}