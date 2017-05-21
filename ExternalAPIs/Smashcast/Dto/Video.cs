using System;
using System.Collections.Generic;
using ExternalAPIs.Smashcast.Converters;
using Newtonsoft.Json;

namespace ExternalAPIs.Smashcast.Dto
{
    public class Video
    {

        [JsonProperty("media_user_name")]
        public string MediaUserName { get; set; }

        [JsonProperty("media_id")]
        public string MediaId { get; set; }

        [JsonProperty("media_file")]
        public string MediaFile { get; set; }

        [JsonProperty("media_user_id")]
        public string MediaUserId { get; set; }

        [JsonProperty("media_profiles")]
        public string MediaProfiles { get; set; }

        [JsonProperty("media_type_id")]
        public string MediaTypeId { get; set; }

        [JsonProperty("media_is_live")]
        [JsonConverter(typeof(BoolConverter))]
        public bool MediaIsLive { get; set; }

        [JsonProperty("media_live_delay")]
        public string MediaLiveDelay { get; set; }

        [JsonProperty("media_date_added")]
        [JsonConverter(typeof(HoribadHitboxDateTimeOffsetConverter))]
        public DateTimeOffset? MediaDateAdded { get; set; }

        [JsonProperty("media_live_since")]
        [JsonConverter(typeof(HoribadHitboxDateTimeOffsetConverter))]
        public DateTimeOffset? MediaLiveSince { get; set; }

        [JsonProperty("media_transcoding")]
        public object MediaTranscoding { get; set; }

        [JsonProperty("media_chat_enabled")]
        [JsonConverter(typeof(BoolConverter))]
        public bool MediaChatEnabled { get; set; }

        [JsonProperty("media_countries")]
        public string MediaCountries { get; set; }

        [JsonProperty("media_hosted_id")]
        public string MediaHostedId { get; set; }

        [JsonProperty("media_mature")]
        [JsonConverter(typeof(BoolConverter))]
        public bool MediaMature { get; set; }

        [JsonProperty("media_hidden")]
        public object MediaHidden { get; set; }

        [JsonProperty("media_offline_id")]
        public object MediaOfflineId { get; set; }

        [JsonProperty("user_banned")]
        public object UserBanned { get; set; }

        [JsonProperty("media_name")]
        public string MediaName { get; set; }

        [JsonProperty("media_display_name")]
        public string MediaDisplayName { get; set; }

        [JsonProperty("media_status")]
        public string MediaStatus { get; set; }

        [JsonProperty("media_title")]
        public string MediaTitle { get; set; }

        [JsonProperty("media_description")]
        public string MediaDescription { get; set; }

        [JsonProperty("media_description_md")]
        public object MediaDescriptionMd { get; set; }

        [JsonProperty("media_tags")]
        public string MediaTags { get; set; }

        [JsonProperty("media_duration")]
        public string MediaDuration { get; set; }

        [JsonProperty("media_bg_image")]
        public string MediaBgImage { get; set; }

        [JsonProperty("media_views", NullValueHandling = NullValueHandling.Ignore)]
        public int MediaViews { get; set; }

        [JsonProperty("media_views_daily")]
        public string MediaViewsDaily { get; set; }

        [JsonProperty("media_views_weekly")]
        public string MediaViewsWeekly { get; set; }

        [JsonProperty("media_views_monthly")]
        public string MediaViewsMonthly { get; set; }

        [JsonProperty("category_id")]
        public string CategoryId { get; set; }

        [JsonProperty("category_name")]
        public string CategoryName { get; set; }

        [JsonProperty("category_name_short")]
        public string CategoryNameShort { get; set; }

        [JsonProperty("category_seo_key")]
        public string CategorySeoKey { get; set; }

        [JsonProperty("category_viewers", NullValueHandling = NullValueHandling.Ignore)]
        public int CategoryViewers { get; set; }

        [JsonProperty("category_media_count", NullValueHandling = NullValueHandling.Ignore)]
        public int CategoryMediaCount { get; set; }

        [JsonProperty("category_channels")]
        public List<string> CategoryChannels { get; set; }

        [JsonProperty("category_logo_small")]
        public string CategoryLogoSmall { get; set; }

        [JsonProperty("category_logo_large")]
        public string CategoryLogoLarge { get; set; }

        [JsonProperty("category_updated")]
        public string CategoryUpdated { get; set; }

        [JsonProperty("team_name")]
        public string TeamName { get; set; }

        [JsonProperty("media_start_in_sec")]
        public string MediaStartInSec { get; set; }

        [JsonProperty("media_duration_format")]
        public string MediaDurationFormat { get; set; }

        [JsonProperty("media_thumbnail")]
        public string MediaThumbnail { get; set; }

        [JsonProperty("media_thumbnail_large")]
        public string MediaThumbnailLarge { get; set; }

        [JsonProperty("channel")]
        public Channel Channel { get; set; }
    }
}