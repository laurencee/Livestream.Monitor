using Newtonsoft.Json;

namespace ExternalAPIs.Hitbox.Dto
{
    public class Following
    {
        [JsonProperty("followers")]
        public int Followers { get; set; }

        [JsonProperty("user_name")]
        public string UserName { get; set; }

        [JsonProperty("user_id")]
        public string UserId { get; set; }

        [JsonProperty("user_logo")]
        public string UserLogo { get; set; }

        [JsonProperty("user_logo_small")]
        public string UserLogoSmall { get; set; }

        [JsonProperty("follow_id")]
        public string FollowId { get; set; }

        [JsonProperty("follower_user_id")]
        public string FollowerUserId { get; set; }

        [JsonProperty("follower_notify")]
        public string FollowerNotify { get; set; }

        [JsonProperty("date_added")]
        public string DateAdded { get; set; }
    }
}