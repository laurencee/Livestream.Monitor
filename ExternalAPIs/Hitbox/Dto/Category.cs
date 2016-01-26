using System;
using ExternalAPIs.Hitbox.Converters;
using Newtonsoft.Json;

namespace ExternalAPIs.Hitbox.Dto
{
    /// <summary> Games for hitbox are listed as categories </summary>
    public class Category
    {
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
        public object CategoryChannels { get; set; }

        [JsonProperty("category_logo_small")]
        public string CategoryLogoSmall { get; set; }

        [JsonProperty("category_logo_large")]
        public string CategoryLogoLarge { get; set; }

        [JsonProperty("category_updated")]
        [JsonConverter(typeof(HoribadHitboxDateTimeOffsetConverter))]
        public DateTimeOffset? CategoryUpdated { get; set; }
    }
}