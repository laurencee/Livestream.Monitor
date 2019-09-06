namespace ExternalAPIs
{
    public class TopStreamQuery : PagedQuery
    {
        public TopStreamQuery()
        {
            Take = 10;
        }

        public string GameName { get; set; }
    }
}