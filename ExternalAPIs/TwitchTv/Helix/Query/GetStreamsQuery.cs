using System.Collections.Generic;

namespace ExternalAPIs.TwitchTv.Helix.Query
{
    public class GetStreamsQuery
    {
        public string CommunityId { get; set; }

        /// <summary> Number of objects to return, max 100, defaulted to 20 </summary>
        public int First { get; set; } = TwitchTvHelixHelixReadonlyClient.DefaultItemsPerQuery;


        public List<string> Languages { get; set; } = new();

        /// <summary> Can specify up to 100 game ids </summary>
        public List<string> GameIds { get; set; } = new();
        
        public List<string> UserIds { get; set; } = new();

        public List<string> UserLogins { get; set; } = new();

        public CursorPagination Pagination { get; set; } = new();
    }
}
