using Newtonsoft.Json;

namespace ExternalAPIs.TwitchTv.V3.Dto
{
    public class Stream
    {
        [JsonProperty("_id")]
        public long? Id { get; set; }

        public string Game { get; set; }

        public int? Viewers { get; set; }

        [JsonProperty("created_at")]
        public string CreatedAt { get; set; }

        [JsonProperty("video_height")]
        public int? VideoHeight { get; set; }

        [JsonProperty("average_fps")]
        public double? AverageFps { get; set; }

        public int? Delay { get; set; }

        [JsonProperty("is_playlist")]
        public bool? IsPlaylist { get; set; }

        public PreviewImage Preview { get; set; }

        public Channel Channel { get; set; }
    }
}