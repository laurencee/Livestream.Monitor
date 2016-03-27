using System;
using Livestream.Monitor.Core;

namespace Livestream.Monitor.Model
{
    /// <summary> A unique definition of a stream specific to an api client. ToString and equality members are implemented. </summary>
    public class UniqueStreamKey
    {
        public UniqueStreamKey(string apiClientName, string streamId)
        {
            if (String.IsNullOrWhiteSpace(apiClientName))
                throw new ArgumentException("Argument is null or whitespace", nameof(apiClientName));
            if (String.IsNullOrWhiteSpace(streamId))
                throw new ArgumentException("Argument is null or whitespace", nameof(streamId));

            ApiClientName = apiClientName;
            StreamId = streamId;
        }

        public string ApiClientName { get; }

        public string StreamId { get; }

        public override string ToString() => $"{ApiClientName}:{StreamId}";

        #region equality members

        protected bool Equals(UniqueStreamKey other)
        {
            return ApiClientName.IsEqualTo(other.ApiClientName) && StreamId.IsEqualTo(other.StreamId);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((UniqueStreamKey) obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((ApiClientName?.GetHashCode() ?? 0) * 397) ^
                       (StreamId == null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(StreamId) * 397);
            }
        }

        #endregion
    }
}