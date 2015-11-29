using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace TwitchTv.Dto
{
    public class Game
    {
        public string Name { get; set; }

        [JsonProperty("_id")]
        public int Id { get; set; }

        public PreviewImage Box { get; set; }

        public PreviewImage Logo { get; set; }
    }
}
