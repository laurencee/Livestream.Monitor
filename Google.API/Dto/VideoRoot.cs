using System.Collections.Generic;

namespace Google.API.Dto
{
    public class VideoRoot
    {
        public string Kind { get; set; }

        public string Etag { get; set; }

        public PageInfo PageInfo { get; set; }

        public List<Item> Items { get; set; }
    }
}