using System.Collections.Generic;
using Newtonsoft.Json;

namespace ExternalAPIs.Smashcast.Dto.QueryRoot
{
    public class UserFollowsRoot
    {
        [JsonProperty("following")]
        public List<Following> Following { get; set; }

        [JsonProperty("max_results")]
        public int MaxResults { get; set; }
    }
}
