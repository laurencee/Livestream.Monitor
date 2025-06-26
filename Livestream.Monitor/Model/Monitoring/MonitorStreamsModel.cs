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
        private readonly HashSet<ChannelIdentifier> channelIdentifiers = new HashSet<ChannelIdentifier>();
        private readonly List<string> ignoredQueryFailures = new List<string>();

        private bool initialised;
        private bool canRefreshLivestreams;
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
                Livestreams.Add(new LivestreamModel("Livestream " + i, null)
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
            this.fileHandler = fileHandler ?? throw new ArgumentNullException(nameof(fileHandler));
            this.settingsHandler = settingsHandler ?? throw new ArgumentNullException(nameof(settingsHandler));
            this.apiClientFactory = apiClientFactory ?? throw new ArgumentNullException(nameof(apiClientFactory));
        }

        public BindableCollection<LivestreamModel> Livestreams { get; } = new BindableCollection<LivestreamModel>();

        public LivestreamModel SelectedLivestream
        {
            get => selectedLivestream;
            set
            {
                if (Equals(value, selectedLivestream)) return;
                selectedLivestream = value;
                NotifyOfPropertyChange();
                NotifyOfPropertyChange(() => CanOpenStream);
            }
        }

        public bool CanOpenStream => selectedLivestream != null && selectedLivestream.Live;

        public bool Initialised
        {
            get => initialised;
            private set
            {
                if (value == initialised) return;
                initialised = value;
                NotifyOfPropertyChange();
            }
        }

        public bool CanRefreshLivestreams
        {
            get => canRefreshLivestreams;
            private set
            {
                if (value == canRefreshLivestreams) return;
                canRefreshLivestreams = value;
                NotifyOfPropertyChange();
            }
        }

        public DateTimeOffset LastRefreshTime
        {
            get => lastRefreshTime;
            private set
            {
                if (value.Equals(lastRefreshTime)) return;
                lastRefreshTime = value;
                NotifyOfPropertyChange(() => LastRefreshTime);
            }
        }

        public event EventHandler LivestreamsRefreshComplete;

        public async Task Initialize(IViewAware viewAware, CancellationToken cancellationToken = default)
        {
            if (Initialised) return;
            var channels = await fileHandler.LoadFromDisk();
            foreach (var channelIdentifier in channels)
            {
                channelIdentifiers.Add(channelIdentifier);
                channelIdentifier.ApiClient.AddChannelWithoutQuerying(channelIdentifier);

                var livestreamModel = new LivestreamModel(channelIdentifier.ChannelId, channelIdentifier);
                if (livestreamModel.ApiClient.ApiName == YoutubeApiClient.API_NAME)
                {
                    livestreamModel.DisplayName = "Youtube Channel: " + (channelIdentifier.DisplayName ?? channelIdentifier.ChannelId);
                }
                else
                {
                    livestreamModel.DisplayName = channelIdentifier.DisplayName ?? channelIdentifier.ChannelId;
                }

                livestreamModel.SetLivestreamNotifyState(settingsHandler.Settings);
                Livestreams.Add(livestreamModel);
                livestreamModel.PropertyChanged += LivestreamModelOnPropertyChanged;
            }

            try
            {
                foreach (var apiClient in apiClientFactory.GetAll())
                {
                    // allows for clearing auth tokens at startup and re-authentication during initialization
                    if (!apiClient.IsAuthorized && Livestreams.Any(x => x.ApiClient == apiClient))
                    {
                        try
                        {
                            await apiClient.Authorize(viewAware);
                        }
                        catch (Exception e)
                        {
                            await viewAware.ShowMessageAsync("Error authorizing API client " + apiClient.ApiName,
                                $"API Client {apiClient.ApiName} authorization failure: " + e.ExtractErrorMessage());
                        }
                    }

                    var result = await apiClient.Initialize(cancellationToken);
                    if (result.ChannelIdentifierDataDirty)
                    {
                        SaveLivestreams();
                    }
                }

                Livestreams.CollectionChanged += FollowedLivestreamsOnCollectionChanged;
                CanRefreshLivestreams = Livestreams.Any();

                await RefreshLivestreams();
            }
            finally // don't stop initialization just due to something failing during stream refreshing
            {
                Initialised = true;
            }
        }

        public async Task AddLivestream(ChannelIdentifier channelIdentifier, IViewAware viewAware)
        {
            if (channelIdentifier == null) throw new ArgumentNullException(nameof(channelIdentifier));
            if (channelIdentifiers.Contains(channelIdentifier)) return; // ignore duplicate requests

            var apiClient = channelIdentifier.ApiClient;
            if (!apiClient.IsAuthorized) await apiClient.Authorize(viewAware);

            var livestreamQueryResults = await apiClient.AddChannel(channelIdentifier);
            livestreamQueryResults.EnsureAllQuerySuccess();

            Livestreams.AddRange(livestreamQueryResults.Select(x => x.LivestreamModel));
            AddChannels(channelIdentifier);
            CanRefreshLivestreams = Livestreams.Any();
        }

        public async Task ImportFollows(string username, IApiClient apiClient, IViewAware viewAware)
        {
            if (username == null) throw new ArgumentNullException(nameof(username));
            if (apiClient == null) throw new ArgumentNullException(nameof(apiClient));
            if (!apiClient.HasFollowedChannelsQuerySupport) throw new InvalidOperationException($"{apiClient.ApiName} does not have support for getting followed streams.");

            if (!apiClient.IsAuthorized) await apiClient.Authorize(viewAware);

            var followedChannelsQueryResults = await apiClient.GetFollowedChannels(username);
            followedChannelsQueryResults.EnsureAllQuerySuccess();

            // Ignore duplicate channels
            var newChannels = followedChannelsQueryResults.Where(x => !channelIdentifiers.Contains(x.ChannelIdentifier)).ToList();
            if (newChannels.Count == 0) return;

            foreach (var newChannel in newChannels)
            {
                newChannel.ChannelIdentifier.ImportedBy = username;
                Livestreams.Add(newChannel.LivestreamModel);
                newChannel.ChannelIdentifier.ApiClient.AddChannelWithoutQuerying(newChannel.ChannelIdentifier);
            }

            AddChannels(newChannels.Select(x => x.ChannelIdentifier).ToArray());
            CanRefreshLivestreams = Livestreams.Any();
            await RefreshLivestreams();
        }

        public async Task RefreshLivestreams()
        {
            if (!CanRefreshLivestreams || 
                (LastRefreshTime > DateTimeOffset.MinValue && LastRefreshTime.AddSeconds(10) >= DateTimeOffset.Now))
            {
                return;
            }

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
                    livestreamQueryResults.EnsureAllQuerySuccess(ignoredQueryFailures);

                    // before the first refresh (or after some network error) all the channels are offline
                    // but we would already have a channel selected
                    // if the selected channel is now online we have to try and update the selected stream quality/allow the user to open the stream
                    NotifyOfPropertyChange(() => CanOpenStream);
                }
            }
            finally
            {
                OnOnlineLivestreamsRefreshComplete();
                // make sure we always update the attempted refresh time and allow refreshing in the future
                LastRefreshTime = DateTimeOffset.Now;
                CanRefreshLivestreams = Livestreams.Any();
            }
        }

        public void IgnoreQueryFailure(string errorToIgnore)
        {
            if (string.IsNullOrWhiteSpace(errorToIgnore))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(errorToIgnore));

            ignoredQueryFailures.Add(errorToIgnore);
        }

        public async Task RemoveLivestream(ChannelIdentifier channelIdentifier, IViewAware viewAware)
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

            // add/remove streams one at a time so we trigger regular add/remove collection change event
            // using addrange/removerange will instead trigger a reset event and will not state what new items were added/removed
            foreach (var livestreamModel in newStreams)
            {
                Livestreams.Add(livestreamModel);
            }
            foreach (var livestreamModel in removedStreams)
            {
                Livestreams.Remove(livestreamModel);
            }
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
                SelectedLivestream = Livestreams.FirstOrDefault(x => x.ChannelIdentifier.Equals(newChannels[0]));
            }
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