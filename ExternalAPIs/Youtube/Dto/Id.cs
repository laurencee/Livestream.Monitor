using Newtonsoft.Json;

namespace ExternalAPIs.Youtube.Dto
{
    public class Id
    {
        [JsonProperty("kind")]
        public string Kind { get; set; }

        [JsonProperty("videoId")]
        public string VideoId { get; set; }
    }
}