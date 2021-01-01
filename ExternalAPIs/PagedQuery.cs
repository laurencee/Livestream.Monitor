using System;

namespace ExternalAPIs
{
    public class PagedQuery : IEquatable<PagedQuery>
    {
        public int Skip { get; set; }

        public int Take { get; set; }

        public bool Equals(PagedQuery other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Skip == other.Skip && Take == other.Take;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((PagedQuery) obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Skip * 397) ^ Take;
            }
        }
    }
}