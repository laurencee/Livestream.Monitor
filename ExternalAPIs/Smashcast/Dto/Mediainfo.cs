using Newtonsoft.Json;

namespace ExternalAPIs.Smashcast.Dto
{
    public class Mediainfo
    {
        [JsonProperty("log_id")]
        public string LogId { get; set; }

        [JsonProperty("media_id")]
        public string MediaId { get; set; }

        [JsonProperty("width")]
        public string Width { get; set; }

        [JsonProperty("height")]
        public string Height { get; set; }

        [JsonProperty("vbitrate")]
        public string Vbitrate { get; set; }

        [JsonProperty("abitrate")]
        public string Abitrate { get; set; }

        [JsonProperty("vcodec")]
        public string Vcodec { get; set; }

        [JsonProperty("acodec")]
        public string Acodec { get; set; }

        [JsonProperty("profile")]
        public string Profile { get; set; }

        [JsonProperty("level")]
        public string Level { get; set; }

        [JsonProperty("fps")]
        public string Fps { get; set; }

        [JsonProperty("gop")]
        public object Gop { get; set; }

        [JsonProperty("kf_interval")]
        public string KfInterval { get; set; }

        [JsonProperty("hostname")]
        public string Hostname { get; set; }

        [JsonProperty("useragent")]
        public object Useragent { get; set; }

        [JsonProperty("log_date")]
        public string LogDate { get; set; }
    }
}