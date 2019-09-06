using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace ExternalAPIs.TwitchTv.V5.Dto.QueryRoot
{
    public partial class GamesRoot
    {
        [JsonProperty("games")]
        public List<Game> Games { get; set; }
    }
}
