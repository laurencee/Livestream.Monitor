using System;

namespace ExternalAPIs.Beam.Pro.Dto
{
    public class Thumbnail
    {
        public Meta meta { get; set; }

        public int id { get; set; }

        public string type { get; set; }

        public int relid { get; set; }

        public string url { get; set; }

        public string store { get; set; }

        public string remotePath { get; set; }

        public DateTimeOffset? createdAt { get; set; }

        public DateTimeOffset? updatedAt { get; set; }
    }
}