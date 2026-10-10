using System;
using Livestream.Monitor.Core;
using Livestream.Monitor.Model.Monitoring;

namespace Livestream.Monitor.Model.ApiClients
{
    public class FailedQueryException : Exception
    {
        public FailedQueryException(ChannelIdentifier channelIdentifier, Exception ex) : base(
            $"Error querying {channelIdentifier.ApiClient.ApiName} channel '{channelIdentifier.DisplayName ?? channelIdentifier.ChannelId}'. {ex.ExtractErrorMessage()}", ex)
        {
            ChannelIdentifier = channelIdentifier;
        }

        /// <summary> A failed request covering every channel of <paramref name="apiClient"/> </summary>
        public FailedQueryException(IApiClient apiClient, Exception ex) : base(
            $"Error querying {apiClient.ApiName} channels. {ex.ExtractErrorMessage()}", ex)
        {
        }

        /// <summary> The failed channel, or null when the whole provider request failed </summary>
        public ChannelIdentifier ChannelIdentifier { get; }
    }
}
