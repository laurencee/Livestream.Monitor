namespace ExternalAPIs.TwitchTv.Helix.Query
{
    public class CursorPagination
    {
        /// <summary>
        /// Cursor for forward pagination: tells the server where to start fetching the next set of results, in a multi-page response. <para />
        /// The cursor value specified here is from the pagination response field of a prior query.
        /// </summary>
        public string After { get; set; }

        /// <summary>
        /// Cursor for backward pagination: tells the server where to start fetching the next set of results, in a multi-page response. <para />
        /// The cursor value specified here is from the pagination response field of a prior query.
        /// </summary>
        public string Before { get; set; }
    }
}