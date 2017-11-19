using System.Collections.Generic;
using Newtonsoft.Json;

namespace ExternalAPIs.Youtube.Dto.QueryRoot
{
    public class GetChannelIdByNameRoot
    {
        [JsonProperty("kind")]
        public string Kind { get; set; }

        [JsonProperty("etag")]
        public string Etag { get; set; }

        [JsonProperty("pageInfo")]
        public PageInfo PageInfo { get; set; }

        [JsonProperty("items")]
        public List<Item> Items { get; set; }
    }
}
