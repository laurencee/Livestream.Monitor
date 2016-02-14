using System.Collections.Generic;

namespace Livestream.Monitor.Model.Monitoring
{
    public interface IMonitoredStreamsFileHandler
    {
        void SaveToDisk(IEnumerable<ChannelIdentifier> livestreams);

        List<ChannelIdentifier> LoadFromDisk();
    }
}