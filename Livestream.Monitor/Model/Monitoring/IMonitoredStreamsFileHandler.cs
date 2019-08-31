using System.Collections.Generic;
using System.Threading.Tasks;

namespace Livestream.Monitor.Model.Monitoring
{
    public interface IMonitoredStreamsFileHandler
    {
        void SaveToDisk(IEnumerable<ChannelIdentifier> livestreams);

        Task<List<ChannelIdentifier>> LoadFromDisk();
    }
}