using System.Collections.Generic;
using Newtonsoft.Json;

namespace ExternalAPIs.Smashcast.Dto.QueryRoot
{
    public class ChannelVideosRoot
    {
        [JsonProperty("media_type")]
        public string MediaType { get; set; }

        [JsonProperty("video")]
        public List<Video> Videos { get; set; }
    }
}
