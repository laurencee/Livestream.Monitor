using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Livestream.Monitor.Model.ApiClients;
using Newtonsoft.Json;

namespace Livestream.Monitor.Model.Monitoring
{
    public class MonitoredStreamsFileHandler : IMonitoredStreamsFileHandler
    {
        private const string FileName = "livestreams.json";
        private readonly IApiClientFactory apiClientFactory;

        public MonitoredStreamsFileHandler(IApiClientFactory apiClientFactory)
        {
            if (apiClientFactory == null) throw new ArgumentNullException(nameof(apiClientFactory));
            this.apiClientFactory = apiClientFactory;
        }

        public void SaveToDisk(IEnumerable<LivestreamModel> livestreams)
        {
            if (livestreams == null) return;

            var livestreamFileData = livestreams.Select(x => new LivestreamFileData()
            {
                LivestreamId = x.Id,
                StreamProvider = x.ApiClient.ApiName,
                ImportedBy = x.ImportedBy
            });
            SaveToDisk(livestreamFileData);
        }

        public List<LivestreamModel> LoadFromDisk()
        {
            if (File.Exists(FileName))
            {
                var livestreamFileData = JsonConvert.DeserializeObject<List<LivestreamFileData>>(File.ReadAllText(FileName));
                return livestreamFileData.Select(fileData => new LivestreamModel()
                {
                    Id = fileData.LivestreamId,
                    ApiClient = apiClientFactory.GetByName(fileData.StreamProvider),
                    ImportedBy = fileData.ImportedBy
                }).ToList();
            }

            return new List<LivestreamModel>();
        }

        private void SaveToDisk(IEnumerable<LivestreamFileData> livestreamFileData)
        {
            File.WriteAllText(FileName, JsonConvert.SerializeObject(livestreamFileData));
        }
    }
}