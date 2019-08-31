namespace ExternalAPIs.TwitchTv.V3.Query
{
    public class TopStreamQuery : PagedQuery
    {
        public TopStreamQuery()
        {
            Take = TwitchTvV3ReadonlyClient.DefaultItemsPerQuery;
        }

        public string GameName { get; set; }
    }
}