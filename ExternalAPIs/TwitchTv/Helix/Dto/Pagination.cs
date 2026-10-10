using System.Text.Json.Serialization;

namespace ExternalAPIs.TwitchTv.Helix.Dto
{
    public class Pagination
    {
        [JsonPropertyName("cursor")]
        public string Cursor { get; set; }
    }
}
