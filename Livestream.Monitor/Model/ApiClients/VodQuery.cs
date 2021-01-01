using System;
using System.Collections.Generic;
using ExternalAPIs;

namespace Livestream.Monitor.Model.ApiClients
{
    public class VodQuery : PagedQuery, IEquatable<VodQuery>
    {
        private string streamId;

        public VodQuery()
        {
            Take = 10;
        }

        public string StreamId
        {
            get { return streamId; }
            set
            {
                if (string.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(StreamId));
                streamId = value;
            }
        }

        /// <summary> 
        /// Arbitrary filtering for vod types. The available types are defined in the <see cref="IApiClient.VodTypes"/> property 
        /// </summary>
        public List<string> VodTypes { get; } = new List<string>();

        public bool Equals(VodQuery other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return base.Equals(other) && streamId == other.streamId && Equals(VodTypes, other.VodTypes);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((VodQuery) obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = base.GetHashCode();
                hashCode = (hashCode * 397) ^ (streamId != null ? streamId.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (VodTypes != null ? VodTypes.GetHashCode() : 0);
                return hashCode;
            }
        }
    }
}