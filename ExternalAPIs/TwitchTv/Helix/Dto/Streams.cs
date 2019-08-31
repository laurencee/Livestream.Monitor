using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace ExternalAPIs.TwitchTv.Helix.Dto
{
    public class StreamsRoot
    {
        [JsonProperty("data")]
        public List<Stream> Streams { get; set; }

        [JsonProperty("pagination")]
        public Pagination Pagination { get; set; }
    }

    public class Stream
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("user_id")]
        public string UserId { get; set; }

        [JsonProperty("user_name")]
        public string UserName { get; set; }

        [JsonProperty("game_id")]
        public string GameId { get; set; }

        [JsonProperty("community_ids")]
        public List<Guid> CommunityIds { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("viewer_count")]
        public long ViewerCount { get; set; }

        [JsonProperty("started_at")]
        public DateTimeOffset? StartedAt { get; set; }

        [JsonProperty("language")]
        public string Language { get; set; }

        /// <summary> Contains a {width} and {height} property that must be replaced to get the actual url </summary>
        [JsonProperty("thumbnail_url")]
        public string ThumbnailTemplateUrl { get; set; }
    }
}
