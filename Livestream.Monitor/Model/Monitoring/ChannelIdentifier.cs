using System;
using Caliburn.Micro;
using Livestream.Monitor.Core;
using Livestream.Monitor.Model.ApiClients;

namespace Livestream.Monitor.Model.Monitoring
{
    /// <summary> Unique identifier of a livestream channel </summary>
    public class ChannelIdentifier
    {
        public ChannelIdentifier()
        {
            if (!Execute.InDesignMode) throw new InvalidOperationException("Design time only constructor");

            ChannelId = "DesignTimeChannel";
            ApiClient = new DesignTimeApiClient();
            ImportedBy = "DesignTime Import";
        }

        public ChannelIdentifier(IApiClient apiClient, string channelId)
        {
            if (string.IsNullOrWhiteSpace(channelId)) throw new ArgumentException("Argument is null or whitespace", nameof(channelId));

            ApiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            ChannelId = channelId;
        }

        public string ChannelId { get; private set; }

        public IApiClient ApiClient { get; }

        public string ImportedBy { get; set; }

        public string DisplayName { get; set; }

        public override string ToString() => $"{ApiClient.ApiName}:{ChannelId}";

        public void OverrideChannelId(string newChannelId)
        {
            ChannelId = newChannelId;
        }

        #region Equality members

        protected bool Equals(ChannelIdentifier other)
        {
            return ChannelId.IsEqualTo(other.ChannelId) && Equals(ApiClient, other.ApiClient);
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
                return (ChannelId == null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(ChannelId) * 397) ^
                       (ApiClient?.GetHashCode() ?? 0);
            }
        }

        #endregion
    }
}