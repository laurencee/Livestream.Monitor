using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExternalAPIs.Beam.Pro.Dto
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
