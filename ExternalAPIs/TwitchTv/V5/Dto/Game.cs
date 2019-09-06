using Newtonsoft.Json;

namespace ExternalAPIs.TwitchTv.V5.Dto
{
    public class Game
    {
        [JsonProperty("_id")]
        public long Id { get; set; }

        [JsonProperty("box")]
        public Box Box { get; set; }

        [JsonProperty("giantbomb_id")]
        public long GiantbombId { get; set; }

        [JsonProperty("logo")]
        public Box Logo { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("popularity")]
        public long Popularity { get; set; }
    }
}