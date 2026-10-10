using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExternalAPIs.TwitchTv.Helix;
using ExternalAPIs.TwitchTv.Helix.Dto;
using ExternalAPIs.TwitchTv.Helix.Query;
using Livestream.Monitor.Core;
using Livestream.Monitor.Model.ApiClients;

namespace Livestream.Monitor.Model.Monitoring
{
    public class MonitoredStreamsFileHandler(IApiClientFactory apiClientFactory) : IMonitoredStreamsFileHandler
    {
        private const string FileName = "livestreams.json";
        private readonly IApiClientFactory apiClientFactory = apiClientFactory ?? throw new ArgumentNullException(nameof(apiClientFactory));
        private bool backUpBeforeNextSave;
        private bool fileBackedUp;

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
                try
                {
                    var livestreamFileText = await File.ReadAllTextAsync(FileName);
                    // An empty file, e.g. left by an interrupted save, has no channels like a missing one.
                    if (string.IsNullOrWhiteSpace(livestreamFileText)) return new List<ChannelIdentifier>();

                    return await ReadChannels(livestreamFileText);
                }
                catch (Exception)
                {
                    // The file could not be read or turned into channels, e.g. locked, invalid JSON or an unknown provider,
                    // so keep a copy to fix by hand before the next save replaces it, unless an upgrade already kept the original.
                    backUpBeforeNextSave = true;
                    throw;
                }
            }

            return new List<ChannelIdentifier>();
        }

        private async Task<List<ChannelIdentifier>> ReadChannels(string livestreamFileText)
        {
            LivestreamFileData livestreamFileData;
            try
            {
                livestreamFileData = JsonSerializer.Deserialize<LivestreamFileData>(livestreamFileText, ExternalAPIs.JsonDefaults.Options);
            }
            catch (JsonException)
            {
                livestreamFileData = new LivestreamFileData();
            }

            if (livestreamFileData.FileVersion == 0)
            {
                await ConvertFileVersion0(livestreamFileText, livestreamFileData);
            }
            if (livestreamFileData.FileVersion == 1)
            {
                ConvertFileVersion1(livestreamFileData);
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
            var twitchTvHelixReadonlyClient = new TwitchTvHelixHelixReadonlyClient();
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

        private void ConvertFileVersion1(LivestreamFileData livestreamFileData)
        {
            livestreamFileData.FileVersion = 2;
            livestreamFileData.LivestreamFileEntries.RemoveAll(x => x.StreamProvider == "smashcast" || x.StreamProvider == "hitbox");

            SaveNewFileVersion(livestreamFileData);
        }

        private async Task ConvertFileVersion0(string livestreamFileText, LivestreamFileData livestreamFileData)
        {
            var fileEntries = JsonSerializer.Deserialize<List<LivestreamFileEntry>>(livestreamFileText, ExternalAPIs.JsonDefaults.Options);

            await ConvertTwitchUsernamesToChannelIds(fileEntries);
            await PopulateDisplayNames(fileEntries);
            livestreamFileData.FileVersion = 1;
            livestreamFileData.LivestreamFileEntries = fileEntries;

            SaveNewFileVersion(livestreamFileData);
        }

        private void SaveNewFileVersion(LivestreamFileData livestreamFileData)
        {
            BackUpFile();
            SaveToDisk(livestreamFileData);
        }

        private void SaveToDisk(LivestreamFileData livestreamFileData)
        {
            if (backUpBeforeNextSave && File.Exists(FileName)) BackUpFile();
            backUpBeforeNextSave = false;

            File.WriteAllText(FileName, JsonSerializer.Serialize(livestreamFileData, ExternalAPIs.JsonDefaults.IndentedOptions));
        }

        // Only the first backup keeps the file as the user had it; later upgrades or saves would copy already changed data.
        private void BackUpFile()
        {
            if (fileBackedUp) return;

            File.Copy(FileName, $"{FileName}_{DateTime.Now:yyyyMMddHHmmss}.bak");
            fileBackedUp = true;
        }
    }
}
