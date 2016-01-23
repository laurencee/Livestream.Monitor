using System;
using System.Collections.Generic;
using TwitchTv.Query;

namespace Livestream.Monitor.Model.StreamProviders
{
    public class VodQuery : PagedQuery
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
        /// Arbitrary filtering for vod types. The available types are defined in the <see cref="IStreamProvider.VodTypes"/> property 
        /// </summary>
        public List<string> VodTypes { get; } = new List<string>();
    }
}