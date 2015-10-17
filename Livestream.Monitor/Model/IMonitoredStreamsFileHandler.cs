using System.Collections.Generic;

namespace Livestream.Monitor.Model
{
    public interface IMonitoredStreamsFileHandler
    {
        void SaveChannelsToDisk(ChannelData[] channels);

        List<ChannelData> LoadChannelsFromDisk();
    }
}