using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExternalAPIs.TwitchTv.Helix.Query
{
    public class GetUsersQuery
    {
        public List<string> UserIds { get; set; } = new List<string>();

        public List<string> UserNames { get; set; } = new List<string>();
    }
}
