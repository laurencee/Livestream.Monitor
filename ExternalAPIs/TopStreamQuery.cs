using System;

namespace ExternalAPIs
{
    public class TopStreamQuery : PagedQuery, IEquatable<TopStreamQuery>
    {
        public TopStreamQuery()
        {
            Take = 10;
        }

        public string GameName { get; set; }

        public bool Equals(TopStreamQuery other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return base.Equals(other) && GameName == other.GameName;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((TopStreamQuery) obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (base.GetHashCode() * 397) ^ (GameName != null ? GameName.GetHashCode() : 0);
            }
        }
    }
}