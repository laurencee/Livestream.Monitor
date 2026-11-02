using System;
using System.Text.Json.Serialization;
using Livestream.Monitor.Core;

namespace Livestream.Monitor.Model
{
    /// <summary> A unique definition of a stream specific to an api client. ToString and equality members are implemented. </summary>
    public class UniqueStreamKey
    {
        public UniqueStreamKey()
        {
            if (ModelEnvironment.IsDesignMode)
            {
                ApiClientName = "Design time api client";
                StreamId = "Design time stream id";
            }
        }

        [JsonConstructor]
        public UniqueStreamKey(string apiClientName, string streamId)
        {
            // would like to verify values here but deserialization of old data can have null values
            // see Settings -> ExcludeNotifyConverter

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
            return Equals((UniqueStreamKey)obj);
        }

        public override int GetHashCode()
        {
            var apiHash = ApiClientName == null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(ApiClientName);
            var streamHash = StreamId == null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(StreamId);

            return HashCode.Combine(apiHash, streamHash);
        }

        #endregion
    }
}
