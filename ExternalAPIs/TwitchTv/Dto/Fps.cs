using Newtonsoft.Json;

namespace ExternalAPIs.TwitchTv.Dto
{
    public class Fps
    {
        [JsonProperty("audio_only")]
        public double AudioOnly { get; set; }

        public double Medium { get; set; }

        public double Mobile { get; set; }

        public double High { get; set; }

        public double Low { get; set; }

        public double Chunked { get; set; }
    }
}