using ExternalAPIs.TwitchTv.Query;

namespace ExternalAPIs.Hitbox.Query
{
    public class TopStreamsQuery : PagedQuery
    {
        public TopStreamsQuery()
        {
            Take = 15;
        }

        /// <summary>  Optional game name filter </summary>
        public string GameName { get; set; }
    }
}