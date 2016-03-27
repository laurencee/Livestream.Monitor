using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Caliburn.Micro;
using Livestream.Monitor.Core;
using Livestream.Monitor.Model.ApiClients;

namespace Livestream.Monitor.Model.Monitoring
{
    public class MonitorStreamsModel : PropertyChangedBase, IMonitorStreamsModel
    {
        private readonly IMonitoredStreamsFileHandler fileHandler;
        private readonly ISettingsHandler settingsHandler;
        private readonly IApiClientFactory apiClientFactory;
        private readonly BindableCollection<LivestreamModel> followedLivestreams = new BindableCollection<LivestreamModel>();
        private readonly HashSet<ChannelIdentifier> channelIdentifiers = new HashSet<ChannelIdentifier>();

        private bool initialised;
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
                followedLivestreams.Add(new LivestreamModel("Livestream " + i, null)
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
            ISettingsHandler settingsHandler,
            IApiClientFactory apiClientFactory)
        {
            if (fileHandler == null) throw new ArgumentNullException(nameof(fileHandler));
            if (settingsHandler == null) throw new ArgumentNullException(nameof(settingsHandler));
            if (apiClientFactory == null) throw new ArgumentNullException(nameof(apiClientFactory));

            this.fileHandler = fileHandler;
            this.settingsHandler = settingsHandler;
            this.apiClientFactory = apiClientFactory;
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

        public event EventHandler LivestreamsRefreshComplete;

        public async Task AddLivestream(ChannelIdentifier channelIdentifier)
        {
            if (channelIdentifier == null) throw new ArgumentNullException(nameof(channelIdentifier));
            if (channelIdentifiers.Contains(channelIdentifier)) return; // ignore duplicate requests

            var livestreamQueryResults = await channelIdentifier.ApiClient.AddChannel(channelIdentifier);
            livestreamQueryResults.EnsureAllQuerySuccess();

            AddChannels(channelIdentifier);
            Livestreams.AddRange(livestreamQueryResults.Select(x => x.LivestreamModel));
        }

        public async Task ImportFollows(string username, IApiClient apiClient)
        {
            if (username == null) throw new ArgumentNullException(nameof(username));
            if (apiClient == null) throw new ArgumentNullException(nameof(apiClient));
            if (!apiClient.HasUserFollowQuerySupport) throw new InvalidOperationException($"{apiClient.ApiName} does not have support for getting followed streams.");

            var followedChannelsQueryResults = await apiClient.GetUserFollows(username);
            followedChannelsQueryResults.EnsureAllQuerySuccess();

            AddChannels(followedChannelsQueryResults.Select(x => x.ChannelIdentifier).ToArray());
            Livestreams.AddRange(followedChannelsQueryResults.Select(x => x.LivestreamModel));
        }

        public async Task RefreshLivestreams()
        {
            if (!CanRefreshLivestreams) return;

            CanRefreshLivestreams = false;

            try
            {
                // query different apis in parallel
                var timeoutTokenSource = new CancellationTokenSource();
                var livestreamQueryResults = (await apiClientFactory.GetAll().ExecuteInParallel(
                    query: apiClient => apiClient.QueryChannels(timeoutTokenSource.Token),
                    timeout: Constants.HalfRefreshPollingTime,
                    cancellationToken: timeoutTokenSource.Token)).SelectMany(x => x).ToList();

                // don't clear out existing results if we get no query results back, that tends to indicate a full query timeout
                if (livestreamQueryResults.Any())
                {
                    var successfulQueries = livestreamQueryResults.Where(x => x.IsSuccess);
                    var failedQueries = livestreamQueryResults.Where(x => !x.IsSuccess);

                    // special identification for failed livestream queries
                    var failedLivestreams = failedQueries.Select(x =>
                    {
                        x.LivestreamModel = new LivestreamModel(x.ChannelIdentifier.ChannelId, x.ChannelIdentifier);
                        x.LivestreamModel.DisplayName = "[ERROR] " + x.ChannelIdentifier.ChannelId;
                        x.LivestreamModel.Description = x.FailedQueryException.Message;
                        return x.LivestreamModel;
                    });

                    var livestreams = successfulQueries.Select(x => x.LivestreamModel).Union(failedLivestreams).ToList();

                    PopulateLivestreams(livestreams);
                    livestreamQueryResults.EnsureAllQuerySuccess();
                }
            }
            finally
            {
                OnOnlineLivestreamsRefreshComplete();
                // make sure we always update the attempted refresh time and allow refreshing in the future
                LastRefreshTime = DateTimeOffset.Now;
                CanRefreshLivestreams = true;
            }
        }

        public async Task RemoveLivestream(ChannelIdentifier channelIdentifier)
        {
            if (channelIdentifier == null) return;

            await channelIdentifier.ApiClient.RemoveChannel(channelIdentifier);
            channelIdentifiers.Remove(channelIdentifier);
            // TODO - if removal of a channel would remove more than 1 livestream, consider warning the user
            var matchingLivestreams = Livestreams.Where(x => Equals(channelIdentifier, x.ChannelIdentifier)).ToList();
            Livestreams.RemoveRange(matchingLivestreams);
            SaveLivestreams();
        }

        protected virtual void OnOnlineLivestreamsRefreshComplete()
        {
            LivestreamsRefreshComplete?.Invoke(this, EventArgs.Empty);
        }

        private void PopulateLivestreams(List<LivestreamModel> livestreamModels)
        {
            foreach (var livestream in livestreamModels)
            {
                var livestreamModel = Livestreams.FirstOrDefault(x => Equals(livestream, x));
                livestreamModel?.PopulateSelf(livestream);
            }

            var newStreams = livestreamModels.Except(Livestreams).ToList();
            var removedStreams = Livestreams.Except(livestreamModels).ToList();

            Livestreams.AddRange(newStreams);
            Livestreams.RemoveRange(removedStreams);
        }

        private void AddChannels(params ChannelIdentifier[] newChannels)
        {
            bool channelAdded = false;
            foreach (var newChannel in newChannels)
            {
                if (channelIdentifiers.Add(newChannel))
                    channelAdded = true;
            }

            if (channelAdded)
            {
                SaveLivestreams();
                SelectedLivestream = Livestreams.First();
            }
        }

        private void LoadLivestreams()
        {
            if (initialised) return;
            foreach (var channelIdentifier in fileHandler.LoadFromDisk())
            {
                channelIdentifiers.Add(channelIdentifier);
                channelIdentifier.ApiClient.AddChannelWithoutQuerying(channelIdentifier);

                // the channel id will have to be replaced when the livestream is queried the first time
                var livestreamModel = new LivestreamModel(channelIdentifier.ChannelId, channelIdentifier);
                // give livestreams some initial displayname before they have been queried
                if (livestreamModel.ApiClient.ApiName == YoutubeApiClient.API_NAME)
                {
                    livestreamModel.DisplayName = "Youtube Channel: " + channelIdentifier.ChannelId;
                }
                else
                {
                    livestreamModel.DisplayName = channelIdentifier.ChannelId;
                }
                
                livestreamModel.SetLivestreamNotifyState(settingsHandler.Settings);
                followedLivestreams.Add(livestreamModel);
                livestreamModel.PropertyChanged += LivestreamModelOnPropertyChanged;
            }

            followedLivestreams.CollectionChanged += FollowedLivestreamsOnCollectionChanged;
            initialised = true;
        }

        private void SaveLivestreams()
        {
            fileHandler.SaveToDisk(channelIdentifiers);
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
                var excludeNotify = livestream.ToExcludeNotify();
                if (livestream.DontNotify)
                    settingsHandler.Settings.ExcludeFromNotifying.Add(excludeNotify);
                else
                    settingsHandler.Settings.ExcludeFromNotifying.Remove(excludeNotify);
            }
        }
    }
}