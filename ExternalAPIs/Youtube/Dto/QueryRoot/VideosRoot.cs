using System.Collections.Generic;

namespace ExternalAPIs.Youtube.Dto.QueryRoot
{
    public class VideosRoot
    {
        public string Kind { get; set; }

        public string Etag { get; set; }

        public PageInfo PageInfo { get; set; }

        public List<Item> Items { get; set; }
    }
}