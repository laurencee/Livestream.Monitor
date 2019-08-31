using Newtonsoft.Json;

namespace ExternalAPIs.TwitchTv.Helix.Dto
{
    public class Pagination
    {
        [JsonProperty("cursor")]
        public string Cursor { get; set; }
    }
}