using System.Collections.Generic;

namespace ExternalAPIs.TwitchTv.Helix.Query
{
    public class GetVideosQuery
    {
        public List<string> VideoIds { get; set; } = new List<string>();

        public string UserId { get; set; }

        public string GameId { get; set; }

        public int First { get; set; } = 15;

        public CursorPagination CursorPagination { get; set; } = new CursorPagination();
    }
}
