using System;
using System.Collections.Generic;
using System.Linq;
using ExternalAPIs;

namespace Livestream.Monitor.Model.ApiClients
{
    public class VodQuery : PagedQuery, IEquatable<VodQuery>
    {
        private string streamDisplayName;

        public VodQuery()
        {
            Take = 10;
        }

        public string StreamDisplayName
        {
            get { return streamDisplayName; }
            set
            {
                if (string.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(StreamDisplayName));
                streamDisplayName = value;
            }
        }

        /// <summary> 
        /// Arbitrary filtering for vod types. The available types are defined in the <see cref="IApiClient.VodTypes"/> property 
        /// </summary>
        public List<string> VodTypes { get; set; } = new List<string>();

        public bool Equals(VodQuery other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return base.Equals(other) && streamDisplayName == other.streamDisplayName && VodTypes.SequenceEqual(other.VodTypes);
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
                hashCode = (hashCode * 397) ^ (streamDisplayName != null ? streamDisplayName.GetHashCode() : 0);
                if (VodTypes != null)
                {
                    foreach (var vodType in VodTypes)
                    {
                        hashCode = (hashCode * 397) ^ (vodType != null ? vodType.GetHashCode() : 0);
                    }
                }
                return hashCode;
            }
        }
    }
}