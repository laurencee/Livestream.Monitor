using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using TwitchTv.Helpers;

namespace TwitchTv.Dto
{
    public class Video
    {
        public string Title { get; set; }

        public string Description { get; set; }

        [JsonProperty("broadcast_id")]
        public object BroadcastId { get; set; }

        public string Status { get; set; }

        [JsonProperty("tag_list")]
        public string TagList { get; set; }

        [JsonProperty("_id")]
        public string Id { get; set; }

        [JsonProperty("recorded_at")]
        public DateTime RecordedAt { get; set; }

        public string Game { get; set; }

        /// <summary>  Length of the broadcast in seconds </summary>
        public double Length { get; set; }

        [JsonProperty("delete_at")]
        public DateTime? DeleteAt { get; set; }

        [JsonProperty("vod_type")]
        public string VodType { get; set; }

        [JsonProperty("is_muted")]
        public bool IsMuted { get; set; }

        public string Preview { get; set; }

        [JsonConverter(typeof(SingleOrArrayThumbnailConverter))]
        public List<Thumbnail> Thumbnails { get; set; }

        public string Url { get; set; }

        public int Views { get; set; }

        public Fps Fps { get; set; }

        public Resolutions Resolutions { get; set; }

        [JsonProperty("broadcast_type")]
        public string BroadcastType { get; set; }

        [JsonProperty("created_at")]
        public DateTime CreatedAt { get; set; }

        public Channel Channel { get; set; }
    }
}