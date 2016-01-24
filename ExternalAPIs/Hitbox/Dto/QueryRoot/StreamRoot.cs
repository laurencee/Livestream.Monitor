using Newtonsoft.Json;

namespace ExternalAPIs.Hitbox.Dto.QueryRoot
{
    public class StreamRoot
    {
        [JsonProperty("mediainfo")]
        public Mediainfo Mediainfo { get; set; }
    }
}
