using System;
using System.Linq;
using System.Threading.Tasks;
using Caliburn.Micro;
using Livestream.Monitor.Core;
using TwitchTv;

namespace Livestream.Monitor.Model.Monitoring
{
    public class MonitorStreamsModel : PropertyChangedBase, IMonitorStreamsModel
    {
        private readonly IMonitoredStreamsFileHandler fileHandler;
        private readonly ITwitchTvReadonlyClient twitchTvClient;
        private readonly BindableCollection<LivestreamModel> followedLivestreams = new BindableCollection<LivestreamModel>();

        private bool initialised;
        private bool canRefreshLivestreams = true;
        private LivestreamModel selectedLivestream;

        #region Design Time Constructor

        /// <summary> Design time only constructor </summary>
        public MonitorStreamsModel()
        {
            if (!Execute.InDesignMode)
                throw new InvalidOperationException("Constructor only accessible from design time");

            var rnd = new Random();

            for (int i = 0; i < 100; i++)
            {
                followedLivestreams.Add(new LivestreamModel()
                {
                    Live = i < 14,
                    DisplayName = $"Channel Name {i + 1}",
                    Description = $"Channel Description {i + 1}",
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

        public BindableCollection<LivestreamModel> Livestreams
        {
            get
            {
                if (!initialised) LoadLivestreams();
                return followedLivestreams;
            }
        }

        public LivestreamModel SelectedLivestream
        {
            get { return selectedLivestream; }
            set
            {
                if (Equals(value, selectedLivestream)) return;
                selectedLivestream = value;
                NotifyOfPropertyChange();
            }
        }

        public bool CanRefreshLivestreams
        {
            get { return canRefreshLivestreams; }
            private set
            {
                if (value == canRefreshLivestreams) return;
                canRefreshLivestreams = value;
                NotifyOfPropertyChange();
            }
        }

        public event EventHandler OnlineLivestreamsRefreshComplete;

        public async Task AddLivestream(LivestreamModel livestreamModel)
        {
            if (livestreamModel == null) throw new ArgumentNullException(nameof(livestreamModel));
            if (Livestreams.Any(x => Equals(x, livestreamModel))) return; // ignore duplicate requests

            // TODO - move this type specific code to a "twitchtv livestream service provider" that would handle calls for twitch livestreams
            var stream = await twitchTvClient.GetStreamDetails(livestreamModel.Id);
            livestreamModel.PopulateWithStreamDetails(stream);
            var channel = await twitchTvClient.GetChannelDetails(livestreamModel.Id);
            livestreamModel.PopulateWithChannel(channel);
            livestreamModel.StreamProvider = StreamProviders.TWITCH_STREAM_PROVIDER;

            Livestreams.Add(livestreamModel);
            SaveLivestreams();
        }

        public async Task ImportFollows(string username)
        {
            if (username == null) throw new ArgumentNullException(nameof(username));
            
            var userFollows = await twitchTvClient.GetUserFollows(username);
            var userFollowedChannels = userFollows.Follows.Select(x => x.Channel.ToLivestreamData(importedBy: username));
            var newChannels = userFollowedChannels.Except(Livestreams); // ignore duplicate channels
            
            Livestreams.AddRange(newChannels);
            SaveLivestreams();
        }

        public async Task RefreshLivestreams()
        {
            if (!CanRefreshLivestreams) return;

            CanRefreshLivestreams = false;
            try
            {
                var onlineStreams = await twitchTvClient.GetStreamsDetails(Livestreams.Select(x => x.Id).ToList());

                foreach (var onlineStream in onlineStreams)
                {
                    var livestream = Livestreams.Single(x => x.Id.IsEqualTo(onlineStream.Channel.Name));

                    livestream.PopulateWithChannel(onlineStream.Channel);
                    livestream.PopulateWithStreamDetails(onlineStream);
                }

                // Notify that the most important livestreams have up to date information
                OnOnlineLivestreamsRefreshComplete();

                var offlineStreams = Livestreams.Where(x => !onlineStreams.Any(y => y.Channel.Name.IsEqualTo(x.Id))).ToList();
                offlineStreams.ForEach(x => x.Offline()); // mark all remaining streams as offline immediately

                var offlineTasks = offlineStreams.Select(x => new
                {
                    Livestream = x,
                    OfflineData = twitchTvClient.GetChannelDetails(x.Id)
                }).ToList();

                await Task.WhenAll(offlineTasks.Select(x => x.OfflineData));
                foreach (var offlineTask in offlineTasks)
                {
                    var offlineData = offlineTask.OfflineData.Result;
                    if (offlineData == null) continue;
                    
                    offlineTask.Livestream.PopulateWithChannel(offlineData);
                }
            }
            catch (Exception)
            {
                // TODO - do something with errors, log/report etc
            }

            CanRefreshLivestreams = true;
        }

        public void RemoveLivestream(LivestreamModel livestreamModel)
        {
            if (livestreamModel == null) return;

            Livestreams.Remove(livestreamModel);
            SaveLivestreams();
        }

        private void LoadLivestreams()
        {
            if (initialised) return;
            var livestreams = fileHandler.LoadFromDisk();
            livestreams.ForEach(x => x.DisplayName = x.Id); // give livestreams some initial displayname before they have been queried
            followedLivestreams.AddRange(livestreams);
            initialised = true;
        }

        private void SaveLivestreams()
        {
            fileHandler.SaveToDisk(Livestreams.ToArray());
        }

        protected virtual void OnOnlineLivestreamsRefreshComplete()
        {
            OnlineLivestreamsRefreshComplete?.Invoke(this, EventArgs.Empty);
        }
    }
}