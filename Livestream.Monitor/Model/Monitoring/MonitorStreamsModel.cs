using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Caliburn.Micro;
using Livestream.Monitor.Core;
using Livestream.Monitor.Model.StreamProviders;
using TwitchTv;

namespace Livestream.Monitor.Model.Monitoring
{
    public class MonitorStreamsModel : PropertyChangedBase, IMonitorStreamsModel
    {
        private readonly IMonitoredStreamsFileHandler fileHandler;
        private readonly ITwitchTvReadonlyClient twitchTvClient;
        private readonly ISettingsHandler settingsHandler;
        private readonly IStreamProviderFactory streamProviderFactory;
        private readonly BindableCollection<LivestreamModel> followedLivestreams = new BindableCollection<LivestreamModel>();

        private bool initialised;
        private bool queryOfflineStreams = true;
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
            ITwitchTvReadonlyClient twitchTvClient,
            ISettingsHandler settingsHandler,
            IStreamProviderFactory streamProviderFactory)
        {
            if (fileHandler == null) throw new ArgumentNullException(nameof(fileHandler));
            if (twitchTvClient == null) throw new ArgumentNullException(nameof(twitchTvClient));
            if (settingsHandler == null) throw new ArgumentNullException(nameof(settingsHandler));
            if (streamProviderFactory == null) throw new ArgumentNullException(nameof(streamProviderFactory));

            this.fileHandler = fileHandler;
            this.twitchTvClient = twitchTvClient;
            this.settingsHandler = settingsHandler;
            this.streamProviderFactory = streamProviderFactory;
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

            var streamProvider = livestreamModel.StreamProvider;
            var offlineStreams = await streamProvider.UpdateOnlineStreams(new List<LivestreamModel>() { livestreamModel });
            if (offlineStreams.Any())
                await streamProvider.UpdateOfflineStreams(new List<LivestreamModel>() { livestreamModel });
            
            livestreamModel.SetLivestreamNotifyState(settingsHandler.Settings);

            Livestreams.Add(livestreamModel);
            SaveLivestreams();

            SelectedLivestream = livestreamModel;
        }

        public async Task ImportFollows(string username)
        {
            if (username == null) throw new ArgumentNullException(nameof(username));

            var userFollows = await twitchTvClient.GetUserFollows(username);
            var userFollowedChannels = from follow in userFollows.Follows
                                       select new LivestreamModel()
                                       {
                                           Id = follow.Channel?.Name,
                                           StreamProvider = streamProviderFactory.Get<TwitchStreamProvider>(),
                                           DisplayName = follow.Channel?.Name,
                                           Description = follow.Channel?.Status,
                                           Game = follow.Channel?.Game,
                                           IsPartner = follow.Channel?.Partner != null && follow.Channel.Partner.Value,
                                           ImportedBy = username,
                                           BroadcasterLanguage = follow.Channel?.BroadcasterLanguage
                                       };

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
                var offlineStreamsProviders = new Dictionary<IStreamProvider, List<LivestreamModel>>();

                foreach (var livestreamProviderGroup in Livestreams.GroupBy(x => x.StreamProvider))
                {
                    var streamProvider = livestreamProviderGroup.Key;
                    var offlineStreams = await streamProvider.UpdateOnlineStreams(livestreamProviderGroup.ToList());
                    offlineStreamsProviders[streamProvider] = offlineStreams;
                }

                // Notify that the most important livestreams (online streams) have up to date information
                OnOnlineLivestreamsRefreshComplete();

                if (offlineStreamsProviders.Any())
                {
                    foreach (var offlineStreamsProvider in offlineStreamsProviders)
                    {
                        offlineStreamsProvider.Value.ForEach(x => x.Offline());

                        if (queryOfflineStreams)
                        {
                            var streamProvider = offlineStreamsProvider.Key;
                            await streamProvider.UpdateOfflineStreams(offlineStreamsProvider.Value);
                        }
                    }

                    queryOfflineStreams = false; // only query offline streams 1 time per application run
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