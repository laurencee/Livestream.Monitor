using System.Collections.Generic;
using Newtonsoft.Json;

namespace ExternalAPIs.TwitchTv.V5.Dto.QueryRoot
{
    public class TopGamesRoot
    {
        [JsonProperty("_total")]
        public long Total { get; set; }

        [JsonProperty("top")]
        public List<TopGame> TopGames { get; set; }
    }
}
