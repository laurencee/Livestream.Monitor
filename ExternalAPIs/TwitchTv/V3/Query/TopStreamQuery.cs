namespace ExternalAPIs.TwitchTv.V3.Query
{
    public class TopStreamQuery : PagedQuery
    {
        public TopStreamQuery()
        {
            Take = TwitchTvReadonlyClient.DefaultItemsPerQuery;
        }

        public string GameName { get; set; }
    }
}