namespace ExternalAPIs.Beam.Pro.Query
{
    public class BeamProPagedQuery : PagedQuery
    {
        public BeamProPagedQuery()
        {
            Take = BeamProReadonlyClient.DefaultItemsPerQuery;
        }
    }
}