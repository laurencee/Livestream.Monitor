using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace ExternalAPIs.TwitchTv.Helix.Dto
{
    public class FollowedChannelsRoot
    {
        [JsonProperty("total")]
        public long Total { get; set; }

        [JsonProperty("data")]
        public List<FollowedChannel> FollowedChannels { get; set; }

        [JsonProperty("pagination")]
        public Pagination Pagination { get; set; }
    }

    public class FollowedChannel
    {
        [JsonProperty("broadcaster_id")]
        public string BroadcasterId { get; set; }

        [JsonProperty("broadcaster_login")]
        public string BroadcasterLogin { get; set; }

        [JsonProperty("broadcaster_name")]
        public string BroadcasterName { get; set; }

        [JsonProperty("followed_at")]
        public DateTime FollowedAt { get; set; }
    }
}
