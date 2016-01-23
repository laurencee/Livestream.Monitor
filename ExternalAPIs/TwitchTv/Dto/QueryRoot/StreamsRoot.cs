using System.Collections.Generic;
using Newtonsoft.Json;

namespace ExternalAPIs.TwitchTv.Dto.QueryRoot
{
    public class StreamsRoot
    {
        [JsonProperty(PropertyName = "_total", NullValueHandling = NullValueHandling.Ignore)]
        public int Total { get; set; }

        public List<Stream> Streams { get; set; }
    }
}