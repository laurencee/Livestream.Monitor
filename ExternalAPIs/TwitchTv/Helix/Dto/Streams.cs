using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ExternalAPIs.TwitchTv.Helix.Dto
{
    public class StreamsRoot
    {
        [JsonPropertyName("data")]
        public List<Stream> Streams { get; set; }

        [JsonPropertyName("pagination")]
        public Pagination Pagination { get; set; }
    }

    public class Stream
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("user_id")]
        public string UserId { get; set; }

        [JsonPropertyName("user_name")]
        public string UserName { get; set; }

        [JsonPropertyName("game_id")]
        public string GameId { get; set; }

        [JsonPropertyName("game_name")]
        public string GameName { get; set; }

        [JsonPropertyName("community_ids")]
        public List<Guid> CommunityIds { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("viewer_count")]
        public long ViewerCount { get; set; }

        [JsonPropertyName("started_at")]
        public DateTimeOffset? StartedAt { get; set; }

        [JsonPropertyName("language")]
        public string Language { get; set; }

        /// <summary> Contains a {width} and {height} property that must be replaced to get the actual url </summary>
        [JsonPropertyName("thumbnail_url")]
        public string ThumbnailTemplateUrl { get; set; }
    }
}
