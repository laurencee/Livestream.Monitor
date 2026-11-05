using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Livestream.Monitor.Core;
using Livestream.Monitor.Core.Settings;
using Livestream.Monitor.Model.ApiClients;

namespace Livestream.Monitor.Model.Monitoring
{
    public class MonitorStreamsModel : ObservableModel, IMonitorStreamsModel
    {
        private readonly IMonitoredStreamsFileHandler fileHandler;
        private readonly ISettingsHandler settingsHandler;
        private readonly IApiClientFactory apiClientFactory;
        private readonly HashSet<ChannelIdentifier> channelIdentifiers = new();
        private readonly List<string> ignoredQueryFailures = new();

        private LivestreamModel selectedLivestream;
        private Task<List<ChannelIdentifier>> initialChannels;
        private readonly Dictionary<IApiClient, Task<List<LivestreamQueryResult>>> initialQueries = new();
        private readonly Dictionary<IApiClient, Task<List<LivestreamQueryResult>>> completeInitialQueries = new();
        private Dictionary<ChannelIdentifier, (string DisplayName, string TwitchLogin)> initialChannelDetails;
        private DateTimeOffset initialQueryStartedAt;
        private bool initialDataDirty;

        #region Design Time Constructor

        /// <summary> Design time only constructor </summary>
        public MonitorStreamsModel()
        {
            if (!ModelEnvironment.IsDesignMode)
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

        public ObservableList<LivestreamModel> Livestreams { get; } = new();

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
            get;
            private set
            {
                if (value == field) return;
                field = value;
                NotifyOfPropertyChange();
            }
        }

        public bool CanRefreshLivestreams
        {
            get;
            private set
            {
                if (value == field) return;
                field = value;
                NotifyOfPropertyChange();
            }
        }

        public DateTimeOffset LastRefreshTime
        {
            get;
            private set
            {
                if (value.Equals(field)) return;
                field = value;
                NotifyOfPropertyChange(() => LastRefreshTime);
            }
        }

        public event EventHandler LivestreamsRefreshComplete;

        public event EventHandler ProviderRefreshComplete;

        /// <summary>
        /// True once followed channels are loaded and every provider with channels has finished its initial query.
        /// <see cref="Initialize"/> then needs no network wait or authorization prompt. False after initialization.
        /// </summary>
        public bool IsInitialQueryComplete
        {
            get
            {
                // The queries are safe to read only after initialChannels completes.
                if (initialChannels is not { IsCompletedSuccessfully: true } || Initialised) return false;

                var channels = initialChannels.Result;
                return apiClientFactory.GetAll().All(apiClient => initialQueries.TryGetValue(apiClient, out var query)
                    ? query.IsCompleted
                    : channels.All(x => x.ApiClient != apiClient));
            }
        }

        // Called on the startup UI thread before constructing the window.
        public void BeginInitialQuery()
        {
            if (initialChannels != null || Initialised) return;

            // Load once before the UI and background query can both access the settings handler.
            _ = settingsHandler.Settings;
            initialQueryStartedAt = DateTimeOffset.UtcNow;
            initialChannels = Task.Run(async () =>
            {
                var channels = await fileHandler.LoadFromDisk();
                initialChannelDetails = channels.Distinct().ToDictionary(x => x, x => (x.DisplayName, x.TwitchLogin));
                foreach (var channel in channels) channel.ApiClient.AddChannelWithoutQuerying(channel);

                foreach (var apiClient in apiClientFactory.GetAll())
                {
                    if (!apiClient.IsAuthorized && !apiClient.CanAuthorizeWithoutUserInput) continue;
                    if (channels.All(x => x.ApiClient != apiClient)) continue;

                    var preparation = PrepareInitially(apiClient);
                    var quick = apiClient.HasQuickQuerySupport;
                    initialQueries.Add(apiClient, QueryInitially(apiClient, channels, preparation, quick));
                    if (quick) completeInitialQueries.Add(apiClient, QueryInitially(apiClient, channels, preparation, quick: false));
                }

                return channels;
            });

            async Task PrepareInitially(IApiClient apiClient)
            {
                using var timeout = new CancellationTokenSource(Constants.HalfRefreshPollingTime);
                if (!apiClient.IsAuthorized) await apiClient.Authorize(screen: null);

                var initialized = await apiClient.Initialize(timeout.Token);
                if (initialized.ChannelIdentifierDataDirty) initialDataDirty = true;
            }

            async Task<List<LivestreamQueryResult>> QueryInitially(
                IApiClient apiClient,
                List<ChannelIdentifier> channels,
                Task preparation,
                bool quick)
            {
                using var timeout = new CancellationTokenSource(Constants.HalfRefreshPollingTime);
                try
                {
                    // Report authorization failures as query failures for this provider's channels.
                    await preparation;
                    return quick ? await apiClient.QueryChannelsQuickly(timeout.Token) : await apiClient.QueryChannels(timeout.Token);
                }
                catch (Exception ex)
                {
                    var failure = new FailedQueryException(apiClient, ex);
                    return channels.Where(x => x.ApiClient == apiClient)
                        .Select(channel => new LivestreamQueryResult(channel) { FailedQueryException = failure })
                        .ToList();
                }
            }
        }

        public async Task Initialize(IUserDialogs viewAware, CancellationToken cancellationToken = default)
        {
            if (Initialised) return;
            var channels = await (initialChannels ?? fileHandler.LoadFromDisk());
            foreach (var channelIdentifier in channels)
            {
                if (channelIdentifiers.Add(channelIdentifier)) channelIdentifier.TwitchLoginChanged += ChannelTwitchLoginChanged;
                if (initialChannels == null) channelIdentifier.ApiClient.AddChannelWithoutQuerying(channelIdentifier);

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
                    if (initialQueries.ContainsKey(apiClient)) continue;

                    // allows for clearing auth tokens at startup and re-authentication during initialization
                    if (!apiClient.IsAuthorized && Livestreams.Any(x => x.ApiClient == apiClient))
                    {
                        try
                        {
                            await apiClient.Authorize(viewAware);
                        }
                        catch (Exception e)
                        {
                            await viewAware.ShowMessage("Error authorizing API client " + apiClient.ApiName,
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

                // Return after each provider's first results; complete queries can finish in the background.
                var initialResultsApplied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var refresh = RefreshLivestreams(initialResultsApplied);
                await Task.WhenAny(initialResultsApplied.Task, refresh);

                // Prefer the initial result task so its query failures aren't lost if both tasks have finished.
                // An empty or early-failing refresh may never complete that task.
                await (initialResultsApplied.Task.IsCompleted ? initialResultsApplied.Task : refresh);
            }
            finally // don't stop initialization just due to something failing during stream refreshing
            {
                Initialised = true;
            }
        }

        public async Task AddLivestream(ChannelIdentifier channelIdentifier, IUserDialogs viewAware)
        {
            if (channelIdentifier == null) throw new ArgumentNullException(nameof(channelIdentifier));
            if (channelIdentifiers.Contains(channelIdentifier)) return; // ignore duplicate requests

            var apiClient = channelIdentifier.ApiClient;
            if (!apiClient.IsAuthorized) await apiClient.Authorize(viewAware);

            var livestreamQueryResults = await apiClient.AddChannel(channelIdentifier);
            livestreamQueryResults.EnsureAllQuerySuccess();
            if (channelIdentifiers.Contains(channelIdentifier)) return;

            Livestreams.AddRange(livestreamQueryResults.Select(x => x.LivestreamModel));
            AddChannels(channelIdentifier);
            CanRefreshLivestreams = Livestreams.Any();
        }

        public async Task ImportFollows(string username, IApiClient apiClient, IUserDialogs viewAware)
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

        public Task RefreshLivestreams() => RefreshLivestreams(initialResultsApplied: null);

        /// <param name="initialResultsApplied">
        /// Set after each provider's first results are applied, or faulted with the query failures.
        /// Complete queries may still be running.
        /// </param>
        private async Task RefreshLivestreams(TaskCompletionSource initialResultsApplied)
        {
            if (!CanRefreshLivestreams ||
                (LastRefreshTime > DateTimeOffset.MinValue && LastRefreshTime.AddSeconds(10) >= DateTimeOffset.Now))
            {
                return;
            }

            CanRefreshLivestreams = false;

            try
            {
                var savedDetails = initialChannelDetails ?? channelIdentifiers.ToDictionary(x => x, x => (x.DisplayName, x.TwitchLogin));
                initialChannelDetails = null;

                // A single deadline cancels every provider request; successful channels still update.
                using var timeoutTokenSource = new CancellationTokenSource(Constants.HalfRefreshPollingTime);
                var cancellationToken = timeoutTokenSource.Token;

                // Providers without channels mustn't count as the first results arriving.
                var queriedProviders = apiClientFactory.GetAll().Where(apiClient => channelIdentifiers.Any(x => x.ApiClient == apiClient));
                var completeQueries = new List<Task<List<LivestreamQueryResult>>>();
                var providerQueries = queriedProviders.Select(QueryProvider).ToList();
                var livestreamQueryResults = new List<LivestreamQueryResult>();

                // Apply results as each provider finishes so slow providers don't hold up the others.
                // ReSharper disable once UseCancellationTokenForIAsyncEnumerable -- collect failure results after the query deadline
                await foreach (var providerQuery in Task.WhenEach(providerQueries))
                    livestreamQueryResults.AddRange(ApplyResults(await providerQuery));

                var queryFailure = livestreamQueryResults.FindQueryFailure(ignoredQueryFailures);

                if (initialResultsApplied != null)
                {
                    if (queryFailure != null) initialResultsApplied.SetException(queryFailure);
                    else initialResultsApplied.SetResult();
                }

                // Keep quick results if the complete query fails; don't report that failure.
                // ReSharper disable once UseCancellationTokenForIAsyncEnumerable -- initial queries have their own deadline
                await foreach (var completeQuery in Task.WhenEach(completeQueries)) ApplyResults(await completeQuery);

                var channelDetailsChanged = channelIdentifiers.Any(x =>
                    savedDetails.TryGetValue(x, out var details) && details != (x.DisplayName, x.TwitchLogin));

                if (initialDataDirty || channelDetailsChanged) SaveLivestreams();
                initialDataDirty = false;

                if (initialResultsApplied == null && queryFailure != null) throw queryFailure;

                List<LivestreamQueryResult> ApplyResults(List<LivestreamQueryResult> queryResults)
                {
                    var providerResults = queryResults.Where(x => channelIdentifiers.Contains(x.ChannelIdentifier)).ToList();

                    // Keep the previous state for channels whose query failed, and still publish the others' changes.
                    PopulateLivestreams(providerResults.Where(x => x.IsSuccess).ToList());

                    NotifyOfPropertyChange(() => CanOpenStream);
                    ProviderRefreshComplete?.Invoke(this, EventArgs.Empty);
                    return providerResults;
                }

                async Task<List<LivestreamQueryResult>> QueryProvider(IApiClient apiClient)
                {
                    var channels = channelIdentifiers.Where(x => x.ApiClient == apiClient).ToList();
                    try
                    {
                        if (initialQueries.Remove(apiClient, out var initialQuery))
                        {
                            completeInitialQueries.Remove(apiClient, out var completeQuery);
                            // An interactive authorization prompt may have made the prefetched results stale.
                            var useInitialResults =
                                DateTimeOffset.UtcNow - initialQueryStartedAt < Constants.HalfRefreshPollingTime;
                            if (useInitialResults && completeQuery != null) completeQueries.Add(completeQuery);

                            // Wait for old requests before re-querying so both can't update provider state at once.
                            var initialResults = await initialQuery;
                            if (useInitialResults) return initialResults;
                            if (completeQuery != null) await completeQuery;
                        }

                        return await apiClient.QueryChannels(cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        var failure = new FailedQueryException(apiClient, ex);
                        return channels.Select(channel => new LivestreamQueryResult(channel) { FailedQueryException = failure }).ToList();
                    }
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

        public async Task RemoveLivestream(ChannelIdentifier channelIdentifier, IUserDialogs viewAware)
        {
            if (channelIdentifier == null) return;

            await channelIdentifier.ApiClient.RemoveChannel(channelIdentifier);
            if (channelIdentifiers.TryGetValue(channelIdentifier, out var monitoredChannel))
                monitoredChannel.TwitchLoginChanged -= ChannelTwitchLoginChanged;

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

        /// <summary> Applies successful query results; only rows of the queried channels are replaced or removed. </summary>
        private void PopulateLivestreams(List<LivestreamQueryResult> successfulQueries)
        {
            var livestreamModels = successfulQueries.Select(x => x.LivestreamModel).ToList();
            foreach (var livestream in livestreamModels)
            {
                var livestreamModel = Livestreams.FirstOrDefault(x => Equals(livestream, x));
                if (!ReferenceEquals(livestreamModel, livestream)) livestreamModel?.PopulateSelf(livestream);
            }

            var newStreams = livestreamModels.Except(Livestreams).ToList();
            var refreshedChannels = new HashSet<ChannelIdentifier>(successfulQueries.Select(x => x.ChannelIdentifier));
            var removedStreams = Livestreams.Where(x => refreshedChannels.Contains(x.ChannelIdentifier)).Except(livestreamModels).ToList();

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
                {
                    newChannel.TwitchLoginChanged += ChannelTwitchLoginChanged;
                    channelAdded = true;
                }
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

        private void ChannelTwitchLoginChanged(object sender, EventArgs e)
        {
            // Refreshes save channel metadata once after applying all providers' results.
            if (CanRefreshLivestreams) SaveLivestreams();
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
