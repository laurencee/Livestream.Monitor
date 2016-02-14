using System;

namespace Livestream.Monitor.Core
{
    public static class Constants
    {
        public static readonly TimeSpan RefreshPollingTime = TimeSpan.FromSeconds(60);
        public static readonly TimeSpan HalfRefreshPollingTime = TimeSpan.FromTicks(RefreshPollingTime.Ticks / 2);
    }
}