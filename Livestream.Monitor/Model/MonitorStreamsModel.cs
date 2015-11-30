using System;
using System.Linq;
using System.Threading.Tasks;
using Caliburn.Micro;
using Livestream.Monitor.Core;
using TwitchTv;

namespace Livestream.Monitor.Model
{
    public class MonitorStreamsModel : PropertyChangedBase, IMonitorStreamsModel
    {
        private readonly IMonitoredStreamsFileHandler fileHandler;
        private readonly ITwitchTvReadonlyClient twitchTvClient;
        private readonly BindableCollection<ChannelData> followedChannels = new BindableCollection<ChannelData>();

        private bool initialised;
        private bool canRefreshChannels = true;
        private ChannelData selectedChannel;

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

        public BindableCollection<ChannelData> Channels
        {
            get
            {
                if (!initialised) LoadChannels();
                return followedChannels;
            }
        }

        public ChannelData SelectedChannel
        {
            get { return selectedChannel; }
            set
            {
                if (Equals(value, selectedChannel)) return;
                selectedChannel = value;
                NotifyOfPropertyChange();
            }
        }

        public bool CanRefreshChannels
        {
            get { return canRefreshChannels; }
            private set
            {
                if (value == canRefreshChannels) return;
                canRefreshChannels = value;
                NotifyOfPropertyChange();
            }
        }

        public event EventHandler OnlineChannelsRefreshComplete;

        public async Task AddStream(ChannelData channelData)
        {
            if (channelData == null) throw new ArgumentNullException(nameof(channelData));
            if (Channels.Any(x => Equals(x, channelData))) return; // ignore duplicate requests

            var stream = await twitchTvClient.GetStreamDetails(channelData.ChannelName);
            channelData.PopulateWithStreamDetails(stream);
            var channel = await twitchTvClient.GetChannelDetails(channelData.ChannelName);
            channelData.PopulateWithChannel(channel);

            Channels.Add(channelData);
            SaveChannels();
        }

        public async Task ImportFollows(string username)
        {
            if (username == null) throw new ArgumentNullException(nameof(username));
            
            var userFollows = await twitchTvClient.GetUserFollows(username);
            var userFollowedChannels = userFollows.Follows.Select(x => x.Channel.ToChannelData(importedBy: username));
            var newChannels = userFollowedChannels.Except(Channels); // ignore duplicate channels

            Channels.AddRange(newChannels);
            SaveChannels();
        }

        public async Task RefreshChannels()
        {
            if (!CanRefreshChannels) return;

            CanRefreshChannels = false;
            try
            {
                var onlineStreams = await twitchTvClient.GetStreamsDetails(Channels.Select(x => x.ChannelName).ToList());

                foreach (var onlineStream in onlineStreams)
                {
                    var channelData = Channels.Single(x => x.ChannelName.IsEqualTo(onlineStream.Channel.Name));

                    channelData.PopulateWithChannel(onlineStream.Channel);
                    channelData.PopulateWithStreamDetails(onlineStream);
                }

                // Notify that the most important channels have up to date information
                OnOnlineChannelsRefreshComplete();

                var offlineStreams = Channels.Where(x => !onlineStreams.Any(y => y.Channel.Name.IsEqualTo(x.ChannelName))).ToList();

                var offlineTasks = offlineStreams.Select(x => new
                {
                    ChannelData = x,
                    OfflineData = twitchTvClient.GetChannelDetails(x.ChannelName)
                }).ToList();

                await Task.WhenAll(offlineTasks.Select(x => x.OfflineData));
                foreach (var offlineTask in offlineTasks)
                {
                    var offlineData = offlineTask.OfflineData.Result;
                    if (offlineData == null) continue;

                    offlineTask.ChannelData.Offline();
                    offlineTask.ChannelData.PopulateWithChannel(offlineData);
                }
            }
            catch (Exception)
            {
                // TODO - do something with errors, log/report etc
            }

            CanRefreshChannels = true;
        }

        public void RemoveChannel(ChannelData channelData)
        {
            if (channelData == null) return;

            Channels.Remove(channelData);
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
            fileHandler.SaveChannelsToDisk(Channels.ToArray());
        }

        protected virtual void OnOnlineChannelsRefreshComplete()
        {
            OnlineChannelsRefreshComplete?.Invoke(this, EventArgs.Empty);
        }
    }
}