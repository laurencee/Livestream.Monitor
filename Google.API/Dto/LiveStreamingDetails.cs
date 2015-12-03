using System;

namespace Google.API.Dto
{
    public class LiveStreamingDetails
    {
        public DateTimeOffset? ActualStartTime { get; set; }

        public DateTimeOffset? ScheduledStartTime { get; set; }

        public int ConcurrentViewers { get; set; }
    }
}