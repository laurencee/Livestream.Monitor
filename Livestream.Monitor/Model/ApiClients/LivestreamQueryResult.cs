using System;
using Livestream.Monitor.Model.Monitoring;

namespace Livestream.Monitor.Model.ApiClients
{
    public class LivestreamQueryResult(ChannelIdentifier channelIdentifier)
    {
        public ChannelIdentifier ChannelIdentifier { get; } = channelIdentifier ?? throw new ArgumentNullException(nameof(channelIdentifier));

        public LivestreamModel LivestreamModel { get; set; }

        public bool IsSuccess => FailedQueryException == null;

        public FailedQueryException FailedQueryException { get; set; }

        public override string ToString()
        {
            return $"{ChannelIdentifier} - IsSuccess: {IsSuccess}";
        }
    }
}