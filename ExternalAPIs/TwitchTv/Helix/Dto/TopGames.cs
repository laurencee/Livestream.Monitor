using System.Collections.Generic;
using Newtonsoft.Json;

namespace ExternalAPIs.TwitchTv.Helix.Dto
{
    public class TopCategoriesRoot
    {
        [JsonProperty("data")]
        public List<TwitchCategory> TopCategories { get; set; }

        [JsonProperty("pagination")]
        public Pagination Pagination { get; set; }
    }

    public class TwitchCategory
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("box_art_url")]
        public string BoxArtUrl { get; set; }
    }
}
