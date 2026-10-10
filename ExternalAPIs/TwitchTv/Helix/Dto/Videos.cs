using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ExternalAPIs.TwitchTv.Helix.Dto
{
    public class VideosRoot
    {
        [JsonPropertyName("data")]
        public List<Video> Videos { get; set; }

        [JsonPropertyName("pagination")]
        public Pagination Pagination { get; set; }
    }

    public class Video
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("user_id")]
        public string UserId { get; set; }

        [JsonPropertyName("user_name")]
        public string UserName { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }

        [JsonPropertyName("created_at")]
        public DateTimeOffset CreatedAt { get; set; }

        [JsonPropertyName("published_at")]
        public DateTimeOffset PublishedAt { get; set; }

        [JsonPropertyName("url")]
        public string Url { get; set; }

        /// <summary> Contains a {width} and {height} property that must be replaced to get the actual url </summary>
        [JsonPropertyName("thumbnail_url")]
        public string ThumbnailTemplateUrl { get; set; }

        [JsonPropertyName("viewable")]
        public string Viewable { get; set; }

        [JsonPropertyName("view_count")]
        public long ViewCount { get; set; }

        [JsonPropertyName("language")]
        public string Language { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("duration")]
        public string Duration { get; set; }
    }
}
