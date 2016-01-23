using System;

namespace ExternalAPIs.TwitchTv.Query
{
    public class PagedQuery
    {
        private int take = TwitchTvReadonlyClient.DefaultItemsPerQuery;

        public int Skip { get; set; }

        /// <summary> Defaults to <see cref="TwitchTvReadonlyClient.DefaultItemsPerQuery"/> </summary>
        public int Take
        {
            get { return take; }
            set
            {
                if (take <= 0) throw new ArgumentOutOfRangeException(nameof(take), "Must request at least 1 query item");
                if (take > 100) throw new ArgumentOutOfRangeException(nameof(take), "Can not request more than 100 query items");
                take = value;
            }
        }
    }
}