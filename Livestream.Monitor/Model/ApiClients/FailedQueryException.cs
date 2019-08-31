using System;
using Livestream.Monitor.Model.Monitoring;

namespace Livestream.Monitor.Model.ApiClients
{
    public class FailedQueryException : Exception
    {
        public FailedQueryException(ChannelIdentifier channelIdentifier, Exception ex) 
            : base($"Error querying {channelIdentifier.ApiClient.ApiName} channel '{channelIdentifier.ChannelId}'. {ex.Message}", ex)
        {
            ChannelIdentifier = channelIdentifier ?? throw new ArgumentNullException(nameof(channelIdentifier));
        }

        public ChannelIdentifier ChannelIdentifier { get; }
    }
}