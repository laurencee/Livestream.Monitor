using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
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
        private readonly ISettingsHandler settingsHandler;
        private readonly BindableCollection<LivestreamModel> followedLivestreams = new BindableCollection<LivestreamModel>();

        private bool initialised;
        private bool canRefreshLivestreams = true;
        private bool queryOfflineStreams = true;
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
            ITwitchTvReadonlyClient twitchTvClient,
            ISettingsHandler settingsHandler)
        {
            if (fileHandler == null) throw new ArgumentNullException(nameof(fileHandler));
            if (twitchTvClient == null) throw new ArgumentNullException(nameof(twitchTvClient));
            if (settingsHandler == null) throw new ArgumentNullException(nameof(settingsHandler));

            this.fileHandler = fileHandler;
            this.twitchTvClient = twitchTvClient;
            this.settingsHandler = settingsHandler;
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
            livestreamModel.SetLivestreamNotifyState(settingsHandler.Settings);
            livestreamModel.StreamProvider = StreamProviders.TWITCH_STREAM_PROVIDER;

            Livestreams.Add(livestreamModel);
            SaveLivestreams();
        }

        public async Task ImportFollows(string username)
        {
            if (username == null) throw new ArgumentNullException(nameof(username));
            
            var userFollows = await twitchTvClient.GetUserFollows(username);
            var userFollowedChannels = userFollows.Follows.Select(x => x.ToLivestreamModel(importedBy: username));
            var newChannels = userFollowedChannels.Except(Livestreams).ToList(); // ignore duplicate channels
            newChannels.ForEach(x => x.SetLivestreamNotifyState(settingsHandler.Settings));

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

                if (queryOfflineStreams)
                {
                    await QueryOfflineStreams(offlineStreams);
                    // We only need to query offline streams one time to get the channel info
                    // It's a waste of resources to query for updates to offline streams 
                    queryOfflineStreams = false;
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

        protected virtual void OnOnlineLivestreamsRefreshComplete()
        {
            OnlineLivestreamsRefreshComplete?.Invoke(this, EventArgs.Empty);
        }

        private async Task QueryOfflineStreams(List<LivestreamModel> offlineStreams)
        {
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

        private void LoadLivestreams()
        {
            if (initialised) return;
            var livestreams = fileHandler.LoadFromDisk();
            
            foreach (var livestream in livestreams)
            {
                livestream.DisplayName = livestream.Id; // give livestreams some initial displayname before they have been queried
                livestream.SetLivestreamNotifyState(settingsHandler.Settings);
            } 

            followedLivestreams.AddRange(livestreams);
            followedLivestreams.CollectionChanged += FollowedLivestreamsOnCollectionChanged;

            foreach (var livestreamModel in followedLivestreams)
            {
                livestreamModel.PropertyChanged += LivestreamModelOnPropertyChanged;
            }
            
            initialised = true;
        }

        private void SaveLivestreams()
        {
            fileHandler.SaveToDisk(Livestreams.ToArray());
        }

        private void FollowedLivestreamsOnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (var livestreamModel in e.NewItems.Cast<LivestreamModel>())
                {
                    livestreamModel.PropertyChanged += LivestreamModelOnPropertyChanged;
                }
            }

            if (e.OldItems != null)
            {
                foreach (var livestreamModel in e.OldItems.Cast<LivestreamModel>())
                {
                    livestreamModel.PropertyChanged -= LivestreamModelOnPropertyChanged;
                }
            }
        }

        private void LivestreamModelOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            var livestream = (LivestreamModel)sender;
            if (e.PropertyName == nameof(LivestreamModel.DontNotify))
            {
                if (livestream.DontNotify)
                    settingsHandler.Settings.ExcludeFromNotifying.Add(livestream.Id);
                else
                    settingsHandler.Settings.ExcludeFromNotifying.Remove(livestream.Id);
            }
        }
    }
}