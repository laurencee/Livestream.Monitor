using System;

namespace ExternalAPIs.Beam.Pro.Dto
{
    public class Vod
    {
        public string storageNode { get; set; }

        public Data data { get; set; }

        public int id { get; set; }

        public string baseUrl { get; set; }

        public string mainUrl { get; set; }

        public string format { get; set; }

        public DateTimeOffset? createdAt { get; set; }

        public DateTimeOffset? updatedAt { get; set; }

        public int recordingId { get; set; }
    }
}