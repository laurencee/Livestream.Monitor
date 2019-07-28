using System.Collections.Generic;
using Newtonsoft.Json;

namespace ExternalAPIs.TwitchTv.V3.Dto.QueryRoot
{
    public class UserFollows
    {
        public List<Follow> Follows { get; set; }

        [JsonProperty(PropertyName = "_total", NullValueHandling = NullValueHandling.Ignore)]
        public int Total { get; set; }
    }
}