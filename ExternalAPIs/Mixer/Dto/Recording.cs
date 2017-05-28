using System;
using System.Collections.Generic;

namespace ExternalAPIs.Mixer.Dto
{
    public class Recording
    {
        public int id { get; set; }

        public string name { get; set; }

        public int? typeId { get; set; }

        public string state { get; set; }

        public int? viewsTotal { get; set; }

        public double duration { get; set; }

        public DateTimeOffset? expiresAt { get; set; }

        public DateTimeOffset? createdAt { get; set; }

        public DateTimeOffset? updatedAt { get; set; }

        public int? channelId { get; set; }

        public List<Vod> vods { get; set; }
    }
}
