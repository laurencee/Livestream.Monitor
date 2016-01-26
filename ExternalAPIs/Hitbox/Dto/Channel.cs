using System;
using ExternalAPIs.Hitbox.Converters;
using Newtonsoft.Json;

namespace ExternalAPIs.Hitbox.Dto
{
    public class Channel
    {
        [JsonProperty("followers", NullValueHandling = NullValueHandling.Ignore)]
        public int Followers { get; set; }

        [JsonProperty("user_id")]
        public string UserId { get; set; }

        [JsonProperty("user_name")]
        public string UserName { get; set; }

        [JsonProperty("user_status")]
        public string UserStatus { get; set; }

        [JsonProperty("user_logo")]
        public string UserLogo { get; set; }

        [JsonProperty("user_cover")]
        public string UserCover { get; set; }

        [JsonProperty("user_logo_small")]
        public string UserLogoSmall { get; set; }

        [JsonProperty("user_partner")]
        public string UserPartner { get; set; }

        [JsonProperty("partner_type")]
        public string PartnerType { get; set; }

        [JsonProperty("user_beta_profile")]
        public string UserBetaProfile { get; set; }

        [JsonProperty("media_is_live")]
        [JsonConverter(typeof(BoolConverter))]
        public bool MediaIsLive { get; set; }

        [JsonProperty("media_live_since")]
        [JsonConverter(typeof(HoribadHitboxDateTimeOffsetConverter))]
        public DateTimeOffset? MediaLiveSince { get; set; }

        [JsonProperty("user_media_id")]
        public string UserMediaId { get; set; }

        [JsonProperty("twitter_account")]
        public string TwitterAccount { get; set; }

        [JsonProperty("twitter_enabled")]
        public string TwitterEnabled { get; set; }

        [JsonProperty("livestream_count")]
        public string LivestreamCount { get; set; }

        [JsonProperty("channel_link")]
        public string ChannelLink { get; set; }
    }
}