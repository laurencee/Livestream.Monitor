using Newtonsoft.Json;

namespace Livestream.Monitor.Model
{
    public class ChannelFileData
    {
        [JsonRequired]
        public string ChannelName { get; set; }

        /// <summary> The username this Channel was imported from </summary>
        public string ImportedBy { get; set; }
    }
}
