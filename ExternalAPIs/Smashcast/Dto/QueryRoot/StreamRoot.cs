using Newtonsoft.Json;

namespace ExternalAPIs.Smashcast.Dto.QueryRoot
{
    public class StreamRoot
    {
        [JsonProperty("mediainfo")]
        public Mediainfo Mediainfo { get; set; }
    }
}
