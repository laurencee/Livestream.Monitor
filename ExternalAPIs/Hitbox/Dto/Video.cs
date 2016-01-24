using Newtonsoft.Json;

namespace ExternalAPIs.Hitbox.Dto
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
        public string MediaIsLive { get; set; }

        [JsonProperty("media_live_delay")]
        public string MediaLiveDelay { get; set; }

        [JsonProperty("media_date_added")]
        public string MediaDateAdded { get; set; }

        [JsonProperty("media_live_since")]
        public object MediaLiveSince { get; set; }

        [JsonProperty("media_transcoding")]
        public object MediaTranscoding { get; set; }

        [JsonProperty("media_chat_enabled")]
        public string MediaChatEnabled { get; set; }

        [JsonProperty("media_countries")]
        public string MediaCountries { get; set; }

        [JsonProperty("media_hosted_id")]
        public object MediaHostedId { get; set; }

        [JsonProperty("media_mature")]
        public object MediaMature { get; set; }

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
        public object MediaBgImage { get; set; }

        [JsonProperty("media_views")]
        public string MediaViews { get; set; }

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
        public object CategoryNameShort { get; set; }

        [JsonProperty("category_seo_key")]
        public string CategorySeoKey { get; set; }

        [JsonProperty("category_viewers")]
        public string CategoryViewers { get; set; }

        [JsonProperty("category_media_count")]
        public string CategoryMediaCount { get; set; }

        [JsonProperty("category_channels")]
        public object CategoryChannels { get; set; }

        [JsonProperty("category_logo_small")]
        public object CategoryLogoSmall { get; set; }

        [JsonProperty("category_logo_large")]
        public string CategoryLogoLarge { get; set; }

        [JsonProperty("category_updated")]
        public string CategoryUpdated { get; set; }

        [JsonProperty("team_name")]
        public object TeamName { get; set; }

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