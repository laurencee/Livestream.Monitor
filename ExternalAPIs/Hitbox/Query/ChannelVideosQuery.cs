using System;

namespace ExternalAPIs.Hitbox.Query
{
    public class ChannelVideosQuery : PagedQuery
    {
        public ChannelVideosQuery(string channelName)
        {
            if (String.IsNullOrEmpty(channelName)) throw new ArgumentException("Argument is null or empty", nameof(channelName));

            ChannelName = channelName;
            Take = 15;
        }

        public string ChannelName { get; private set; }
    }
}