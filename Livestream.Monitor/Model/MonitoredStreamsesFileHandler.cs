using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace Livestream.Monitor.Model
{
    public class MonitoredStreamsesFileHandler : IMonitoredStreamsFileHandler
    {
        public void SaveChannelsToDisk(ChannelData[] channels)
        {
            if (channels == null || channels.Length <= 0) return;

            var channelFileData = channels.Select(x => x.ToChannelFileData()).ToArray();
            File.WriteAllText("channels.json", JsonConvert.SerializeObject(channelFileData));
        }

        public List<ChannelData> LoadChannelsFromDisk()
        {
            if (File.Exists("channels.json"))
            {
                var channelFileData = JsonConvert.DeserializeObject<List<ChannelFileData>>(File.ReadAllText("channels.json"));
                return channelFileData.Select(x => x.ToChannelData()).ToList();
            }

            return new List<ChannelData>();
        }
    }
}