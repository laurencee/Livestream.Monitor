using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExternalAPIs.TwitchTv.Helix;
using ExternalAPIs.TwitchTv.Helix.Dto;
using ExternalAPIs.TwitchTv.Helix.Query;
using Livestream.Monitor.Core;
using Livestream.Monitor.Model.ApiClients;
using Newtonsoft.Json;

namespace Livestream.Monitor.Model.Monitoring
{
    public class MonitoredStreamsFileHandler : IMonitoredStreamsFileHandler
    {
        private const string FileName = "livestreams.json";
        private readonly IApiClientFactory apiClientFactory;
        private readonly ITwitchTvHelixReadonlyClient twitchTvHelixReadonlyClient;

        public MonitoredStreamsFileHandler(
            IApiClientFactory apiClientFactory,
            ITwitchTvHelixReadonlyClient twitchTvHelixReadonlyClient)
        {
            this.apiClientFactory = apiClientFactory ?? throw new ArgumentNullException(nameof(apiClientFactory));
            this.twitchTvHelixReadonlyClient = twitchTvHelixReadonlyClient ?? throw new ArgumentNullException(nameof(twitchTvHelixReadonlyClient));
        }

        public void SaveToDisk(IEnumerable<ChannelIdentifier> livestreams)
        {
            if (livestreams == null) return;

            var livestreamFileEntries = livestreams.Select(x => new LivestreamFileEntry()
            {
                ChannelId = x.ChannelId,
                StreamProvider = x.ApiClient.ApiName,
                ImportedBy = x.ImportedBy,
                DisplayName = x.DisplayName
            }).ToList();

            var livestreamFileData = new LivestreamFileData()
            {
                FileVersion = LivestreamFileData.CurrentFileVersion,
                LivestreamFileEntries = livestreamFileEntries
            };

            SaveToDisk(livestreamFileData);
        }

        public async Task<List<ChannelIdentifier>> LoadFromDisk()
        {
            if (File.Exists(FileName))
            {
                var livestreamFileText = File.ReadAllText(FileName);
                LivestreamFileData livestreamFileData;
                try
                {
                    livestreamFileData = JsonConvert.DeserializeObject<LivestreamFileData>(livestreamFileText);
                }
                catch (JsonSerializationException)
                {
                    livestreamFileData = new LivestreamFileData();
                }
                
                if (livestreamFileData.FileVersion == 0)
                {
                    var fileEntries = JsonConvert.DeserializeObject<List<LivestreamFileEntry>>(livestreamFileText);

                    await ConvertTwitchUsernamesToChannelIds(fileEntries);
                    await PopulateDisplayNames(fileEntries);
                    livestreamFileData.FileVersion = LivestreamFileData.CurrentFileVersion;
                    livestreamFileData.LivestreamFileEntries = fileEntries;
                    
                    // in case something went wrong in the conversion make a copy of the existing file first
                    File.Copy(FileName, $"{FileName}_{DateTime.Now:yyyyMMddHHmmss}.bak");
                    SaveToDisk(livestreamFileData);
                }

                return livestreamFileData.LivestreamFileEntries.Select(entry =>
                {
                    var apiClient = apiClientFactory.GetByName(entry.StreamProvider);
                    return new ChannelIdentifier(apiClient, entry.ChannelId)
                    {
                        ImportedBy = entry.ImportedBy,
                        DisplayName = entry.DisplayName
                    };
                }).ToList();
            }

            return new List<ChannelIdentifier>();
        }

        private async Task PopulateDisplayNames(List<LivestreamFileEntry> fileEntries)
        {
            var groupedLivestreams = fileEntries.Where(x => x.StreamProvider != TwitchApiClient.API_NAME).GroupBy(x => x.StreamProvider);
            foreach (var groupedLivestream in groupedLivestreams)
            {
                var tempChannels = new List<ChannelIdentifier>();
                var apiClient = apiClientFactory.GetByName(groupedLivestream.Key);
                foreach (var entry in groupedLivestream)
                {
                    var channelId = new ChannelIdentifier(apiClient, entry.ChannelId)
                    {
                        DisplayName = entry.DisplayName,
                        ImportedBy = entry.ImportedBy
                    };
                    apiClient.AddChannelWithoutQuerying(channelId);
                    tempChannels.Add(channelId);
                }

                var queriedChannels = await apiClient.QueryChannels(CancellationToken.None);
                foreach (var entry in groupedLivestream)
                {
                    var matchingChannel = queriedChannels.FirstOrDefault(x => x.ChannelIdentifier.ChannelId.IsEqualTo(entry.ChannelId));
                    if (matchingChannel != null) entry.DisplayName = matchingChannel.LivestreamModel.DisplayName;
                    else fileEntries.Remove(entry); // will have to be manually re-imported, most likely the channel has been removed/banned
                }

                tempChannels.ForEach(x => apiClient.RemoveChannel(x));
            }
        }

        private async Task ConvertTwitchUsernamesToChannelIds(List<LivestreamFileEntry> livestreamFileEntries)
        {
            var twitchStreams = livestreamFileEntries.Where(x => x.StreamProvider == TwitchApiClient.API_NAME).ToList();

            var twitchUsers = new List<User>();
            var query = new GetUsersQuery();
            query.UserNames.AddRange(twitchStreams.Select(x => x.ChannelId));
            var users = await twitchTvHelixReadonlyClient.GetUsers(query);
            twitchUsers.AddRange(users);

            foreach (var twitchStream in twitchStreams)
            {
                var matchingUser = twitchUsers.FirstOrDefault(x => x.Login.IsEqualTo(twitchStream.ChannelId));
                if (matchingUser != null)
                {
                    twitchStream.DisplayName = matchingUser.DisplayName;
                    twitchStream.ChannelId = matchingUser.Id;
                }
                else
                {
                    livestreamFileEntries.Remove(twitchStream); // will have to be manually re-imported, most likely the channel has been removed/banned
                }
            }
        }

        private void SaveToDisk(LivestreamFileData livestreamFileData)
        {
            File.WriteAllText(FileName, JsonConvert.SerializeObject(livestreamFileData, Formatting.Indented));
        }
    }
}