using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Caliburn.Micro;
using ExternalAPIs.TwitchTv;
using Livestream.Monitor.Core;
using Livestream.Monitor.Model.StreamProviders;

namespace Livestream.Monitor.Model.Monitoring
{
    public class MonitorStreamsModel : PropertyChangedBase, IMonitorStreamsModel
    {
        public static readonly TimeSpan RefreshPollingTime = TimeSpan.FromSeconds(60);
        public static readonly TimeSpan HalfRefreshPollingTime = TimeSpan.FromTicks(RefreshPollingTime.Ticks / 2);

        private readonly IMonitoredStreamsFileHandler fileHandler;
        private readonly ITwitchTvReadonlyClient twitchTvClient;
        private readonly ISettingsHandler settingsHandler;
        private readonly IStreamProviderFactory streamProviderFactory;
        private readonly BindableCollection<LivestreamModel> followedLivestreams = new BindableCollection<LivestreamModel>();

        private bool initialised;
        private bool queryOfflineStreams = true;
        private bool canRefreshLivestreams = true;
        private LivestreamModel selectedLivestream;
        private DateTimeOffset lastRefreshTime;

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

        public DateTimeOffset LastRefreshTime
        {
            get { return lastRefreshTime; }
            private set
            {
                if (value.Equals(lastRefreshTime)) return;
                lastRefreshTime = value;
                NotifyOfPropertyChange(() => LastRefreshTime);
            }
        }

        public event EventHandler OnlineLivestreamsRefreshComplete;

        public async Task AddLivestream(LivestreamModel livestreamModel)
        {
            if (livestreamModel == null) throw new ArgumentNullException(nameof(livestreamModel));
            if (Livestreams.Any(x => Equals(x, livestreamModel))) return; // ignore duplicate requests

            var timeoutTokenSource = new CancellationTokenSource();
            var streamProvider = livestreamModel.StreamProvider;
            var offlineStreams = await streamProvider.UpdateOnlineStreams(new List<LivestreamModel>() { livestreamModel }, timeoutTokenSource.Token);
            if (offlineStreams.Any())
            {
                timeoutTokenSource = new CancellationTokenSource();
                await streamProvider.UpdateOfflineStreams(new List<LivestreamModel>() { livestreamModel }, timeoutTokenSource.Token);
            }

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

                // query different stream providers online streams in parallel
                var timeoutTokenSource = new CancellationTokenSource();
                var tasks = GetQueryOnlineStreamTasks(offlineStreamsProviders, timeoutTokenSource.Token);
                await Task.WhenAny(Task.WhenAll(tasks), Task.Delay(HalfRefreshPollingTime, timeoutTokenSource.Token));

                // Notify that the most important livestreams (online streams) have up to date information
                OnOnlineLivestreamsRefreshComplete();

                // reinitialize the cancellation token source for offline querying
                timeoutTokenSource = new CancellationTokenSource();
                var queryOfflineStreamsTasks = new List<Task>();
                foreach (var offlineStreamsProvider in offlineStreamsProviders)
                {
                    offlineStreamsProvider.Value.ForEach(x => x.Offline());

                    if (queryOfflineStreams)
                    {
                        var streamProvider = offlineStreamsProvider.Key;
                        var queryOfflineStreamsTask = streamProvider.UpdateOfflineStreams(offlineStreamsProvider.Value, timeoutTokenSource.Token)
                                                                    .TimeoutAfter(HalfRefreshPollingTime);
                        queryOfflineStreamsTasks.Add(queryOfflineStreamsTask);
                    }
                }

                // query different stream providers offline streams in parallel
                await Task.WhenAll(queryOfflineStreamsTasks);
                queryOfflineStreams = false; // only query offline streams 1 time per application run
            }
            catch (Exception)
            {
                // TODO - do something with errors, log/report etc
            }

            LastRefreshTime = DateTimeOffset.Now;
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

        private List<Task> GetQueryOnlineStreamTasks(
            Dictionary<IStreamProvider, List<LivestreamModel>> offlineStreamsProviders,
            CancellationToken cancellationToken)
        {
            var tasks = new List<Task>();

            foreach (var livestreamProviderGroup in Livestreams.GroupBy(x => x.StreamProvider))
            {
                var streamProvider = livestreamProviderGroup.Key;
                var queryTask = streamProvider.UpdateOnlineStreams(livestreamProviderGroup.ToList(), cancellationToken)
                                              .TimeoutAfter(HalfRefreshPollingTime);

                queryTask.ContinueWith(task =>
                {
                    var offlineStreams = task.Result;
                    offlineStreamsProviders[streamProvider] = offlineStreams;
                }, cancellationToken);
                tasks.Add(queryTask);
            }
            return tasks;
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