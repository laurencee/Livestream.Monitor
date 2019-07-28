using System.Collections.Generic;
using Newtonsoft.Json;

namespace ExternalAPIs.Youtube.Dto.QueryRoot
{
    public class SearchLiveVideosRoot
    {
        [JsonProperty("kind")]
        public string Kind { get; set; }

        [JsonProperty("etag")]
        public string Etag { get; set; }

        [JsonProperty("pageInfo")]
        public PageInfo PageInfo { get; set; }

        [JsonProperty("items")]
        public List<SearchItemResult> Items { get; set; }
    }
}
