using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExternalAPIs.Beam.Pro.Dto
{
    public class Channel
    {
        public int? tetrisGameId { get; set; }
        public int id { get; set; }
        public int userId { get; set; }
        public string token { get; set; }
        public bool online { get; set; }
        public bool featured { get; set; }
        public bool partnered { get; set; }
        public int? transcodingProfileId { get; set; }
        public bool suspended { get; set; }
        public string name { get; set; }
        public string audience { get; set; }
        public int viewersTotal { get; set; }
        public int viewersCurrent { get; set; }
        public int numFollowers { get; set; }
        public string description { get; set; }
        public int? typeId { get; set; }
        public bool interactive { get; set; }
        public int? interactiveGameId { get; set; }
        public int ftl { get; set; }
        public bool hasVod { get; set; }
        public object languageId { get; set; }
        public int? coverId { get; set; }
        public int? thumbnailId { get; set; }
        public int? badgeId { get; set; }
        public object hosteeId { get; set; }
        public bool hasTranscodes { get; set; }
        public bool vodsEnabled { get; set; }
        public Thumbnail thumbnail { get; set; }
        public User user { get; set; }
        public Type type { get; set; }        
    }
}
