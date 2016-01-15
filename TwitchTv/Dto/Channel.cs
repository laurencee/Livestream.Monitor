using Newtonsoft.Json;

namespace TwitchTv.Dto
{
    public class Channel
    {
        [JsonProperty("_links")]
        public ChannelLinks ChannelLinks { get; set; }

        public object Background { get; set; }

        public object Banner { get; set; }

        [JsonProperty("broadcaster_language")]
        public string BroadcasterLanguage { get; set; }

        [JsonProperty("display_name")]
        public string DisplayName { get; set; }

        public string Game { get; set; }

        public string Logo { get; set; }

        public bool? Mature { get; set; }

        public string Status { get; set; }

        public bool? Partner { get; set; }

        public string Url { get; set; }

        [JsonProperty("video_banner")]
        public string VideoBanner { get; set; }

        [JsonProperty("_id")]
        public int? Id { get; set; }

        public string Name { get; set; }

        [JsonProperty("created_at")]
        public string CreatedAt { get; set; }

        [JsonProperty("updated_at")]
        public string UpdatedAt { get; set; }

        public int? Delay { get; set; }

        public int? Followers { get; set; }

        [JsonProperty("profile_banner")]
        public string ProfileBanner { get; set; }

        [JsonProperty("profile_banner_background_color")]
        public string ProfileBannerBackgroundColor { get; set; }

        public int? Views { get; set; }

        public string Language { get; set; }
    }
}