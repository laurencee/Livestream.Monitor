namespace ExternalAPIs.Mixer.Query
{
    public class MixerPagedQuery : PagedQuery
    {
        public MixerPagedQuery()
        {
            Take = MixerReadonlyClient.DefaultItemsPerQuery;
        }
    }
}