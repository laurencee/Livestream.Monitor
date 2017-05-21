using Newtonsoft.Json;

namespace Livestream.Monitor.Model.Monitoring
{
    public class LivestreamFileData
    {
        private string streamProvider;

        /// <summary> 
        /// This value needs to represent the channel rather than the stream to support youtube
        /// which allows multiple livestreams concurrently from the 1 channel.
        /// </summary>
        [JsonRequired]
        [JsonProperty("LivestreamId")]
        public string ChannelId { get; set; }

        /// <summary> The username this livestream was imported from </summary>
        public string ImportedBy { get; set; }

        /// <summary> The site which this livestream belongs to (twitch/youtube etc.) </summary>
        [JsonRequired]
        public string StreamProvider
        {
            get
            {
                // convert stream providers who have changed names to allow for old stored names
                if (streamProvider == "hitbox")
                    return "smashcast";
                return streamProvider;
            }
            set { streamProvider = value; }
        }
    }
}
