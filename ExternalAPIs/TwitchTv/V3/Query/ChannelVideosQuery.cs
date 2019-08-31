using System;

namespace ExternalAPIs.TwitchTv.V3.Query
{
    public class ChannelVideosQuery : PagedQuery
    {
        private string channelName;

        public ChannelVideosQuery()
        {
            Take = 10; // change default number of vods to take since most people will be looking for recent vods
        }

        public string ChannelName
        {
            get { return channelName; }
            set
            {
                channelName = value ?? throw new ArgumentNullException(nameof(ChannelName));
            }
        }

        /// <summary> Return only broadcasts if true, otherwise return only highlights </summary>
        public bool BroadcastsOnly { get; set; }

        /// <summary> Returns only HLS VoDs when true, otherwise only non-hls vods are returned </summary>
        public bool HLSVodsOnly { get; set; }
    }
}
