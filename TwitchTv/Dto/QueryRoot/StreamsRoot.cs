using System.Collections.Generic;
using Newtonsoft.Json;

namespace TwitchTv.Dto.QueryRoot
{
    public class StreamsRoot
    {
        [JsonProperty(PropertyName = "_total")]
        public int Total { get; set; }

        public List<Stream> Streams { get; set; }
    }
}