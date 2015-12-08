using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace Livestream.Monitor.Model.Monitoring
{
    public class MonitoredStreamsFileHandler : IMonitoredStreamsFileHandler
    {
        private const string OldFileName = "channels.json";
        private const string FileName = "livestreams.json";

        public void SaveToDisk(IEnumerable<LivestreamModel> livestreams)
        {
            if (livestreams == null) return;

            var livestreamFileData = livestreams.Select(x => x.ToLivestreamFileData());
            SaveToDisk(livestreamFileData);
        }

        public List<LivestreamModel> LoadFromDisk()
        {
            if (File.Exists(OldFileName)) MigrateOldFile();

            if (File.Exists(FileName))
            {
                var livestreamFileData = JsonConvert.DeserializeObject<List<LivestreamFileData>>(File.ReadAllText(FileName));
                return livestreamFileData.Select(x => x.ToLivestreamData()).ToList();
            }

            return new List<LivestreamModel>();
        }

        private void SaveToDisk(IEnumerable<LivestreamFileData> livestreamFileData)
        {
            File.WriteAllText(FileName, JsonConvert.SerializeObject(livestreamFileData));
        }

        private void MigrateOldFile()
        {
            var oldLivestreamFileFormat = JsonConvert.DeserializeObject<List<OldFileDefinition>>(File.ReadAllText(OldFileName));
            var livestreamFileFormat = (from oldFormat in oldLivestreamFileFormat
                                       select new LivestreamFileData()
                                       {
                                           LivestreamId = oldFormat.ChannelName,
                                           ImportedBy = oldFormat.ImportedBy,
                                           StreamProvider = StreamProviders.TWITCH_STREAM_PROVIDER
                                       }).ToList();

            File.Delete(OldFileName);
            SaveToDisk(livestreamFileFormat);
        }

        private class OldFileDefinition
        {
            [JsonRequired]
            public string ChannelName { get; set; }

            /// <summary> The username this Channel was imported from </summary>
            public string ImportedBy { get; set; }
        }
    }
}