using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExternalAPIs.TwitchTv.Helix.Query
{
    public class GetGamesQuery
    {
        public List<string> GameIds { get; set; } = new List<string>();

        public List<string> GameNames { get; set; } = new List<string>();
    }
}
