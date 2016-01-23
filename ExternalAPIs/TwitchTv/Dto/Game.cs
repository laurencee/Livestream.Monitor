using Newtonsoft.Json;

namespace ExternalAPIs.TwitchTv.Dto
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
