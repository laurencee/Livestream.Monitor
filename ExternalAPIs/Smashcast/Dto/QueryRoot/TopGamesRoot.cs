using System.Collections.Generic;
using Newtonsoft.Json;

namespace ExternalAPIs.Smashcast.Dto.QueryRoot
{
    public class TopGamesRoot
    {
        [JsonProperty("categories")]
        public List<Category> Categories { get; set; }
    }
}
