using System;
using Newtonsoft.Json;

namespace ExternalAPIs.TwitchTv.V5.Dto
{
    public class Box
    {
        [JsonProperty("large")]
        public Uri Large { get; set; }

        [JsonProperty("medium")]
        public Uri Medium { get; set; }

        [JsonProperty("small")]
        public Uri Small { get; set; }

        [JsonProperty("template")]
        public string Template { get; set; }
    }
}