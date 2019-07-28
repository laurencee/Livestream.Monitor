using System.Collections.Generic;
using Newtonsoft.Json;

namespace ExternalAPIs.TwitchTv.V3.Dto.QueryRoot
{
    public class ChannelVideosRoot
    {
        [JsonProperty(PropertyName = "_total", NullValueHandling = NullValueHandling.Ignore)]
        public int Total { get; set; }

        public List<Video> Videos { get; set; }
    }
}
