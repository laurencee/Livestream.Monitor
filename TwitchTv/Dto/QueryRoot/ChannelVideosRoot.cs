using System.Collections.Generic;
using Newtonsoft.Json;

namespace TwitchTv.Dto.QueryRoot
{
    public class ChannelVideosRoot
    {
        [JsonProperty(PropertyName = "_total")]
        public int Total { get; set; }

        public List<Video> Videos { get; set; }
    }
}
