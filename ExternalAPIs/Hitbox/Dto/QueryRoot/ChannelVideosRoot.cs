using System.Collections.Generic;
using Newtonsoft.Json;

namespace ExternalAPIs.Hitbox.Dto.QueryRoot
{
    public class ChannelVideosRoot
    {
        [JsonProperty("media_type")]
        public string MediaType { get; set; }

        [JsonProperty("video")]
        public List<Video> Videos { get; set; }
    }
}
