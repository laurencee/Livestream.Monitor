using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace ExternalAPIs.TwitchTv.Helix.Dto
{
    public class UserFollowsRoot
    {
        [JsonProperty("total")]
        public long Total { get; set; }

        [JsonProperty("data")]
        public List<UserFollow> UserFollows { get; set; }

        [JsonProperty("pagination")]
        public Pagination Pagination { get; set; }
    }

    public class UserFollow
    {
        [JsonProperty("from_id")]
        public string FromId { get; set; }

        [JsonProperty("from_name")]
        public string FromName { get; set; }

        [JsonProperty("to_id")]
        public string ToId { get; set; }

        [JsonProperty("to_name")]
        public string ToName { get; set; }

        [JsonProperty("followed_at")]
        public DateTimeOffset FollowedAt { get; set; }
    }
}
