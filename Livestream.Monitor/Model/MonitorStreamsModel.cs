using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Caliburn.Micro;
using TwitchTv;
using static System.String;

namespace Livestream.Monitor.Model
{
    public class MonitorStreamsModel : IMonitorStreamsModel
    {
        private readonly IMonitoredStreamsFileHandler fileHandler;
        private readonly ITwitchTvReadonlyClient twitchTvClient;
        private readonly BindableCollection<ChannelData> followedChannels = new BindableCollection<ChannelData>();

        private bool initialised;

        #region Design Time Constructor

        /// <summary> Design time only constructor </summary>
        public MonitorStreamsModel()
        {
            if (!Execute.InDesignMode)
                throw new InvalidOperationException("Constructor only accessible from design time");

            var rnd = new Random();

            for (int i = 0; i < 100; i++)
            {
                followedChannels.Add(new ChannelData()
                {
                    Live = i < 14,
                    ChannelName = $"Channel Name {i + 1}",
                    ChannelDescription = $"Channel Description {i + 1}",
                    Game = i < 50 ? "Game A" : "Game B",
                    StartTime = i < 14 ? DateTimeOffset.Now.AddSeconds(-(rnd.Next(10000))) : DateTimeOffset.MinValue,
                    Viewers = i < 14 ? rnd.Next(50000) : 0
                });
            }
        }

        #endregion

        public MonitorStreamsModel(
            IMonitoredStreamsFileHandler fileHandler,
            ITwitchTvReadonlyClient twitchTvClient)
        {
            if (fileHandler == null) throw new ArgumentNullException(nameof(fileHandler));
            if (twitchTvClient == null) throw new ArgumentNullException(nameof(twitchTvClient));

            this.fileHandler = fileHandler;
            this.twitchTvClient = twitchTvClient;
        }

        public BindableCollection<ChannelData> FollowedChannels
        {
            get
            {
                if (!initialised) LoadChannels();
                return followedChannels;
            }
        }

        public event EventHandler OnlineChannelsRefreshComplete;

        public async Task AddStream(ChannelData channelData)
        {
            if (channelData == null) throw new ArgumentNullException(nameof(channelData));
            if (FollowedChannels.Any(x => Equals(x, channelData))) return; // ignore duplicate requests

            var stream = await twitchTvClient.GetStreamDetails(channelData.ChannelName);
            channelData.PopulateWithStreamDetails(stream);
            FollowedChannels.Add(channelData);
            SaveChannels();
        }

        public async Task ImportFollows(string username)
        {
            if (username == null) throw new ArgumentNullException(nameof(username));
            
            var userFollows = await twitchTvClient.GetUserFollows(username);
            var userFollowedChannels = userFollows.Follows.Select(x => x.Channel.ToChannelData(importedBy: username));
            var newChannels = userFollowedChannels.Except(FollowedChannels); // ignore duplicate channels

            FollowedChannels.AddRange(newChannels);
            SaveChannels();
        }

        public async Task RefreshChannels()
        {
            var missingChannelData = new List<ChannelData>();
            var tasks = FollowedChannels.Where(x => !IsNullOrWhiteSpace(x.ChannelName))
                                        .Select(x => new { ChannelData = x, Stream = twitchTvClient.GetStreamDetails(x.ChannelName) })
                                        .ToList();

            try
            {
                await Task.WhenAll(tasks.Select(x => x.Stream));
                foreach (var task in tasks)
                {
                    var streamDetails = task.Stream.Result;
                    if (streamDetails == null)
                    {
                        missingChannelData.Add(task.ChannelData);
                        if (task.ChannelData.Live) // streamer is no longer live
                        {
                            task.ChannelData.Offline();
                        }
                        continue;
                    }

                    task.ChannelData.PopulateWithChannel(streamDetails.Channel);
                    task.ChannelData.PopulateWithStreamDetails(streamDetails);
                }

                // Notify that the most important channels have up to date information
                OnOnlineChannelsRefreshComplete();

                var offlineTasks = missingChannelData.Select(x => new
                {
                    ChannelData = x,
                    OfflineData = twitchTvClient.GetChannelDetails(x.ChannelName)
                }).ToList();

                await Task.WhenAll(offlineTasks.Select(x => x.OfflineData));
                foreach (var offlineTask in offlineTasks)
                {
                    var offlineData = offlineTask.OfflineData.Result;
                    if (offlineData == null) continue;

                    offlineTask.ChannelData.PopulateWithChannel(offlineData);
                }
            }
            catch (Exception)
            {
                // TODO - do something with errors, log/report etc
            }
        }

        public void RemoveChannel(ChannelData channelData)
        {
            if (channelData == null) return;

            FollowedChannels.Remove(channelData);
            SaveChannels();
        }

        private void LoadChannels()
        {
            if (initialised) return;
            followedChannels.AddRange(fileHandler.LoadChannelsFromDisk());
            initialised = true;
        }

        private void SaveChannels()
        {
            fileHandler.SaveChannelsToDisk(FollowedChannels.ToArray());
        }

        protected virtual void OnOnlineChannelsRefreshComplete()
        {
            OnlineChannelsRefreshComplete?.Invoke(this, EventArgs.Empty);
        }
    }
}