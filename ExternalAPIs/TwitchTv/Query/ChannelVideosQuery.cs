using System;

namespace ExternalAPIs.TwitchTv.Query
{
    /// <summary>
    /// Default Take size
    /// </summary>
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
                if (value == null) throw new ArgumentNullException(nameof(ChannelName));
                channelName = value;
            }
        }

        /// <summary> Return only broadcasts if true, otherwise return only highlights </summary>
        public bool BroadcastsOnly { get; set; }

        /// <summary> Returns only HLS VoDs when true, otherwise only non-hls vods are returned </summary>
        public bool HLSVodsOnly { get; set; }
    }
}
