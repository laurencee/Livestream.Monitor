using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ExternalAPIs.TwitchTv.Helix.Dto
{
    public class FollowedChannelsRoot
    {
        [JsonPropertyName("total")]
        public long Total { get; set; }

        [JsonPropertyName("data")]
        public List<FollowedChannel> FollowedChannels { get; set; }

        [JsonPropertyName("pagination")]
        public Pagination Pagination { get; set; }
    }

    public class FollowedChannel
    {
        [JsonPropertyName("broadcaster_id")]
        public string BroadcasterId { get; set; }

        [JsonPropertyName("broadcaster_login")]
        public string BroadcasterLogin { get; set; }

        [JsonPropertyName("broadcaster_name")]
        public string BroadcasterName { get; set; }

        [JsonPropertyName("followed_at")]
        public DateTime FollowedAt { get; set; }
    }
}
