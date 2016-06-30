using System.Collections.Generic;
using Newtonsoft.Json;

namespace ExternalAPIs.TwitchTv.Dto.QueryRoot
{
    public class TopGamesRoot
    {
        [JsonProperty(PropertyName = "_total", NullValueHandling = NullValueHandling.Ignore)]
        public int Total { get; set; }

        [JsonProperty(PropertyName = "top")]
        public List<TopGame> TopGames { get; set; }
    }
}
