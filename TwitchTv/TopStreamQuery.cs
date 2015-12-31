using System;

namespace TwitchTv
{
    public class TopStreamQuery
    {
        private int take = TwitchTvReadonlyClient.DefaultItemsPerQuery;

        public string GameName { get; set; }

        public int Skip { get; set; }

        /// <summary> Defaults to <see cref="TwitchTvReadonlyClient.DefaultItemsPerQuery"/> </summary>
        public int Take
        {
            get { return take; }
            set
            {
                if (take <= 0) throw new ArgumentOutOfRangeException(nameof(take), "Top stream query minimum request size is 1");
                if (take > 100) throw new ArgumentOutOfRangeException(nameof(take), "Top stream query maximum request size is 100");
                take = value;
            }
        }
    }
}