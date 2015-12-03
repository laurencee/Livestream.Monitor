using System.Collections.Generic;

namespace Livestream.Monitor.Model
{
    public interface IMonitoredStreamsFileHandler
    {
        void SaveToDisk(IEnumerable<LivestreamModel> livestreams);

        List<LivestreamModel> LoadFromDisk();
    }
}