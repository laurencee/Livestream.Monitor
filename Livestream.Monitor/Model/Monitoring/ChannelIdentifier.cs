using System;
using Livestream.Monitor.Model.ApiClients;

namespace Livestream.Monitor.Model.Monitoring
{
    /// <summary> Unique identifier of a livestream channel </summary>
    public class ChannelIdentifier
    {
        public ChannelIdentifier(IApiClient apiClient, string channelId)
        {
            if (apiClient == null) throw new ArgumentNullException(nameof(apiClient));
            if (string.IsNullOrWhiteSpace(channelId)) throw new ArgumentException("Argument is null or whitespace", nameof(channelId));

            ApiClient = apiClient;
            ChannelId = channelId;
        }

        public string ChannelId { get; }

        public IApiClient ApiClient { get; }

        public string ImportedBy { get; set; }

        public override string ToString() => $"{ApiClient.ApiName}:{ChannelId}";

        #region Equality members

        protected bool Equals(ChannelIdentifier other)
        {
            return string.Equals(ChannelId, other.ChannelId) && Equals(ApiClient, other.ApiClient);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((ChannelIdentifier) obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((ChannelId?.GetHashCode() ?? 0) * 397) ^ (ApiClient?.GetHashCode() ?? 0);
            }
        }

        #endregion
    }
}