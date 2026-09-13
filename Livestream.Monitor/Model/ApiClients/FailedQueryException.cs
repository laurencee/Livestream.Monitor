using System;
using Livestream.Monitor.Core;
using Livestream.Monitor.Model.Monitoring;

namespace Livestream.Monitor.Model.ApiClients
{
    public class FailedQueryException(ChannelIdentifier channelIdentifier, Exception ex) : Exception(
        $"Error querying {channelIdentifier.ApiClient.ApiName} channel '{channelIdentifier.ChannelId}'. {ex.ExtractErrorMessage()}", ex)
    {
        public ChannelIdentifier ChannelIdentifier { get; } = channelIdentifier ?? throw new ArgumentNullException(nameof(channelIdentifier));
    }
}