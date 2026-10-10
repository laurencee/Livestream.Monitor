using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ExternalAPIs.TwitchTv.Helix.Dto
{
    public class GamesRoot
    {
        [JsonPropertyName("data")]
        public List<Game> Games { get; set; }
    }

    public class Game
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("box_art_url")]
        public string BoxArtUrl { get; set; }
    }
}
