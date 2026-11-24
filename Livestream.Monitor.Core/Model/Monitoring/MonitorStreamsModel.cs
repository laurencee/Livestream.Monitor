using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Livestream.Monitor.Core;
using Livestream.Monitor.Core.Settings;
using Livestream.Monitor.Model.ApiClients;

namespace Livestream.Monitor.Model.Monitoring
{
    public class MonitorStreamsModel : IMonitorStreamsModel
    {
        private readonly IMonitoredStreamsFileHandler fileHandler;
        private readonly ISettingsHandler settingsHandler;
        private readonly IApiClientFactory apiClientFactory;
        private readonly TimeProvider timeProvider;
        private readonly HashSet<ChannelIdentifier> channelIdentifiers = new();
        private readonly List<string> ignoredQueryFailures = new();

        private Task<List<ChannelIdentifier>> initialChannels;
        // The prefetch task registers its queries before returning the followed list to Initialize.
        private readonly Dictionary<IApiClient, InitialQuery> initialQueries = new();
        private Dictionary<ChannelIdentifier, (string DisplayName, string TwitchLogin)> initialChannelDetails;
        private long initialQueryStartedAt;
        private bool prefetchedResultsExpired;
        private bool initialDataDirty;
        private DateTimeOffset lastRefreshTime;

        public MonitorStreamsModel(
            IMonitoredStreamsFileHandler fileHandler,
            ISettingsHandler settingsHandler,
            IApiClientFactory apiClientFactory)
            : this(fileHandler, settingsHandler, apiClientFactory, TimeProvider.System) { }

        internal MonitorStreamsModel(
            IMonitoredStreamsFileHandler fileHandler,
            ISettingsHandler settingsHandler,
            IApiClientFactory apiClientFactory,
            TimeProvider timeProvider)
        {
            this.fileHandler = fileHandler ?? throw new ArgumentNullException(nameof(fileHandler));
            this.settingsHandler = settingsHandler ?? throw new ArgumentNullException(nameof(settingsHandler));
            this.apiClientFactory = apiClientFactory ?? throw new ArgumentNullException(nameof(apiClientFactory));
            this.timeProvider = timeProvider;
        }

        public ObservableCollection<LivestreamModel> Livestreams { get; } = new();

        public bool Initialised { get; private set; }
        public bool CanRefreshLivestreams { get; private set; }

        public event EventHandler LivestreamsRefreshComplete;

        public event Action<LivestreamNotification> CategoryChangeNotification;

        public event EventHandler ProviderRefreshComplete;

        // Called on the startup UI thread before constructing the window.
        public void BeginInitialQuery()
        {
            if (initialChannels != null || Initialised) return;

            // Load once before the UI and background query can both access the settings handler.
            _ = settingsHandler.Settings;
            initialQueryStartedAt = timeProvider.GetTimestamp();
            initialChannels = Task.Run(async () =>
            {
                var channels = await LoadInitialChannels();

                foreach (var apiClient in apiClientFactory.GetAll())
                {
                    if (!apiClient.IsAuthorized && !apiClient.CanAuthorizeWithoutUserInput) continue;

                    var followed = channels.Where(channel => channel.ApiClient == apiClient).ToList();
                    if (followed.Count != 0)
                        initialQueries.Add(apiClient, QueryInitially(apiClient, followed, dialogs: null, quick: apiClient.HasQuickQuerySupport));
                }

                return channels;
            });
        }

        public async Task Initialize(IUserDialogs dialogs, CancellationToken cancellationToken = default)
        {
            if (Initialised) return;

            var channels = await (initialChannels ?? LoadInitialChannels());
            foreach (var channelIdentifier in channels)
            {
                if (channelIdentifiers.Add(channelIdentifier)) channelIdentifier.TwitchLoginChanged += ChannelTwitchLoginChanged;

                var livestreamModel = new LivestreamModel(channelIdentifier.ChannelId, channelIdentifier);
                if (livestreamModel.ApiClient.ApiName == YoutubeApiClient.API_NAME)
                {
                    livestreamModel.DisplayName = "Youtube Channel: " + (channelIdentifier.DisplayName ?? channelIdentifier.ChannelId);
                }
                else
                {
                    livestreamModel.DisplayName = channelIdentifier.DisplayName ?? channelIdentifier.ChannelId;
                }

                Livestreams.Add(livestreamModel);
            }

            try
            {
                var prefetchedProviders = initialQueries.Keys.ToArray();
                var otherInitialization = new List<Task>();
                foreach (var apiClient in apiClientFactory.GetAll())
                {
                    if (initialQueries.ContainsKey(apiClient)) continue;

                    var followed = channels.Where(channel => channel.ApiClient == apiClient).ToList();
                    if (followed.Count == 0) otherInitialization.Add(InitializeUnusedProvider(apiClient));
                    else initialQueries.Add(apiClient, QueryInitially(apiClient, followed, dialogs, quick: false, cancellationToken));
                }

                var initializedUnusedProviders = Task.WhenAll(otherInitialization);
                if (initialQueries.Count == 0)
                {
                    await initializedUnusedProviders;
                    return;
                }

                // Start both tasks before awaiting either, so results that are ready reach the UI during authorization.
                var initialResultsApplied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var refresh = ApplyInitialResults(initialResultsApplied, prefetchedProviders);
                await Task.WhenAll(initializedUnusedProviders, WaitForInitialResults());

                async Task WaitForInitialResults()
                {
                    await Task.WhenAny(initialResultsApplied.Task, refresh);
                    await (initialResultsApplied.Task.IsCompleted ? initialResultsApplied.Task : refresh);
                }
            }
            finally // don't stop initialization just due to something failing during stream refreshing
            {
                Initialised = true;
            }

            async Task InitializeUnusedProvider(IApiClient apiClient)
            {
                var result = await apiClient.Initialize(cancellationToken);
                if (result.ChannelIdentifierDataDirty) SaveLivestreams();
            }
        }

        private async Task<List<ChannelIdentifier>> LoadInitialChannels()
        {
            var channels = await fileHandler.LoadFromDisk();
            initialChannelDetails = channels.Distinct().ToDictionary(x => x, x => (x.DisplayName, x.TwitchLogin));
            foreach (var channel in channels) channel.ApiClient.AddChannelWithoutQuerying(channel);

            return channels;
        }

        private InitialQuery QueryInitially(
            IApiClient apiClient,
            List<ChannelIdentifier> channels,
            IUserDialogs dialogs,
            bool quick,
            CancellationToken cancellationToken = default)
        {
            var needsPrompt = !apiClient.IsAuthorized && !apiClient.CanAuthorizeWithoutUserInput;
            var preparation = Prepare();
            var primary = Query(quick);
            var complete = quick ? Query(quickly: false) : null;
            return new InitialQuery(primary, complete);

            async Task Prepare()
            {
                if (needsPrompt)
                {
                    try
                    {
                        await apiClient.Authorize(dialogs);
                    }
                    catch (Exception exception)
                    {
                        var title = "Error authorizing API client " + apiClient.ApiName;
                        var message = $"API Client {apiClient.ApiName} authorization failure: " + exception.ExtractErrorMessage();
                        await dialogs.ShowMessage(title, message);
                    }

                    if (initialChannels != null && timeProvider.GetElapsedTime(initialQueryStartedAt) >= Constants.HalfRefreshPollingTime)
                        prefetchedResultsExpired = true;
                }

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(Constants.HalfRefreshPollingTime);
                if (!apiClient.IsAuthorized && apiClient.CanAuthorizeWithoutUserInput) await apiClient.Authorize(screen: null);

                var initialized = await apiClient.Initialize(timeout.Token);
                if (initialized.ChannelIdentifierDataDirty) initialDataDirty = true;
            }

            async Task<List<LivestreamQueryResult>> Query(bool quickly)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                if (!needsPrompt) timeout.CancelAfter(Constants.HalfRefreshPollingTime);

                try
                {
                    await preparation;
                    if (needsPrompt) timeout.CancelAfter(Constants.HalfRefreshPollingTime);

                    return quickly ? await apiClient.QueryChannelsQuickly(timeout.Token) : await apiClient.QueryChannels(timeout.Token);
                }
                catch (Exception exception)
                {
                    return FailedChannels(apiClient, channels, exception);
                }
            }
        }

        private async Task ApplyInitialResults(TaskCompletionSource initialResultsApplied, IApiClient[] prefetchedProviders)
        {
            CanRefreshLivestreams = false;
            var pending = initialQueries.Values.ToList();
            var pendingPrimary = pending.Count;
            var followedAtStart = channelIdentifiers.ToDictionary(channel => channel);
            var primaryResults = new List<LivestreamQueryResult>();
            var recheckPrefetchedResults = false;

            try
            {
                while (pending.Count != 0)
                {
                    var finished = await Task.WhenAny(pending.Select(query => query.Results));
                    var index = pending.FindIndex(query => query.Results == finished);
                    var query = pending[index];
                    pending.RemoveAt(index);

                    var results = await finished;
                    var applied = ApplyResults(results.Where(result => IsStillFollowed(result.ChannelIdentifier, followedAtStart)));
                    if (query.IsComplete) continue;

                    // The complete query can run alongside the quick one, but its results only become eligible for publishing
                    // once the same provider's quick results are applied.
                    if (query.Complete != null) pending.Add(new InitialQuery(query.Complete, IsComplete: true));

                    primaryResults.AddRange(applied);
                    if (--pendingPrimary != 0) continue;

                    recheckPrefetchedResults = prefetchedResultsExpired && prefetchedProviders.Length != 0;
                    if (!recheckPrefetchedResults) ReportPrimaryResults();
                }

                if (recheckPrefetchedResults)
                {
                    // Keep the displayed rows while refreshing, but report only the recheck's outcome for these providers.
                    primaryResults.RemoveAll(result => prefetchedProviders.Contains(result.ChannelIdentifier.ApiClient));
                    await RecheckPrefetchedProviders();
                    ReportPrimaryResults();
                }

                SaveChangedChannelDetails(initialChannelDetails);
            }
            finally
            {
                initialQueries.Clear();
                initialChannelDetails = null;
                FinishRefresh();
            }

            void ReportPrimaryResults()
            {
                var failure = primaryResults.FindQueryFailure(ignoredQueryFailures);
                if (failure != null) initialResultsApplied.SetException(failure);
                else initialResultsApplied.SetResult();
            }

            async Task RecheckPrefetchedProviders()
            {
                // Recheck after their startup queries settle. Failed channels keep their prior rows.
                using var timeout = new CancellationTokenSource(Constants.HalfRefreshPollingTime);
                var cancellationToken = timeout.Token;
                var followedAtRecheck = channelIdentifiers.ToDictionary(channel => channel);
                var queries = prefetchedProviders.Select(provider => QueryProvider(provider, cancellationToken));

                // ReSharper disable once UseCancellationTokenForIAsyncEnumerable -- collect results from every query after the shared deadline
                await foreach (var query in Task.WhenEach(queries))
                {
                    var results = await query;
                    var currentResults = results.Where(result => IsStillFollowed(result.ChannelIdentifier, followedAtRecheck));
                    primaryResults.AddRange(ApplyResults(currentResults));
                }
            }

            // A channel removed and added again can compare equal while belonging to a newer query.
            bool IsStillFollowed(ChannelIdentifier channel, Dictionary<ChannelIdentifier, ChannelIdentifier> followedAtQueryStart) =>
                followedAtQueryStart.TryGetValue(channel, out var original) &&
                channelIdentifiers.TryGetValue(channel, out var followed) &&
                ReferenceEquals(original, followed);
        }

        private sealed record InitialQuery(
            Task<List<LivestreamQueryResult>> Results,
            Task<List<LivestreamQueryResult>> Complete = null,
            bool IsComplete = false);

        public async Task AddLivestream(ChannelIdentifier channelIdentifier, IUserDialogs dialogs)
        {
            if (channelIdentifier == null) throw new ArgumentNullException(nameof(channelIdentifier));
            if (channelIdentifiers.Contains(channelIdentifier)) return; // ignore duplicate requests

            var apiClient = channelIdentifier.ApiClient;
            if (!apiClient.IsAuthorized) await apiClient.Authorize(dialogs);

            var livestreamQueryResults = await apiClient.AddChannel(channelIdentifier);
            livestreamQueryResults.EnsureAllQuerySuccess();
            if (channelIdentifiers.Contains(channelIdentifier)) return;

            foreach (var result in livestreamQueryResults) Livestreams.Add(result.LivestreamModel);

            AddChannels(channelIdentifier);
            CanRefreshLivestreams = initialQueries.Count == 0 && Livestreams.Any();
        }

        public async Task ImportFollows(string username, IApiClient apiClient, IUserDialogs dialogs)
        {
            if (username == null) throw new ArgumentNullException(nameof(username));
            if (apiClient == null) throw new ArgumentNullException(nameof(apiClient));
            if (!apiClient.HasFollowedChannelsQuerySupport) throw new InvalidOperationException($"{apiClient.ApiName} does not have support for getting followed streams.");

            if (!apiClient.IsAuthorized) await apiClient.Authorize(dialogs);

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
            CanRefreshLivestreams = initialQueries.Count == 0 && Livestreams.Any();
            await RefreshLivestreams();
        }

        public async Task RefreshLivestreams()
        {
            if (!CanRefreshLivestreams ||
                (lastRefreshTime > DateTimeOffset.MinValue && lastRefreshTime.AddSeconds(10) >= DateTimeOffset.Now))
            {
                return;
            }

            CanRefreshLivestreams = false;

            try
            {
                var savedDetails = channelIdentifiers.ToDictionary(x => x, x => (x.DisplayName, x.TwitchLogin));

                // A single deadline cancels every provider request; successful channels still update.
                using var timeoutTokenSource = new CancellationTokenSource(Constants.HalfRefreshPollingTime);
                var cancellationToken = timeoutTokenSource.Token;

                // Providers without channels mustn't count as the first results arriving.
                var queriedProviders = apiClientFactory.GetAll().Where(apiClient => channelIdentifiers.Any(x => x.ApiClient == apiClient));
                var providerQueries = queriedProviders.Select(provider => QueryProvider(provider, cancellationToken)).ToList();
                var livestreamQueryResults = new List<LivestreamQueryResult>();

                // Apply results as each provider finishes so slow providers don't hold up the others.
                // ReSharper disable once UseCancellationTokenForIAsyncEnumerable -- collect failure results after the query deadline
                await foreach (var providerQuery in Task.WhenEach(providerQueries))
                    livestreamQueryResults.AddRange(ApplyResults(await providerQuery));

                SaveChangedChannelDetails(savedDetails);
                livestreamQueryResults.EnsureAllQuerySuccess(ignoredQueryFailures);
            }
            finally
            {
                FinishRefresh();
            }
        }

        private async Task<List<LivestreamQueryResult>> QueryProvider(IApiClient apiClient, CancellationToken cancellationToken)
        {
            var channels = channelIdentifiers.Where(channel => channel.ApiClient == apiClient).ToList();
            if (channels.Count == 0) return [];

            try
            {
                return await apiClient.QueryChannels(cancellationToken);
            }
            catch (Exception exception)
            {
                return FailedChannels(apiClient, channels, exception);
            }
        }

        private List<LivestreamQueryResult> ApplyResults(IEnumerable<LivestreamQueryResult> queryResults)
        {
            var providerResults = queryResults.Where(x => channelIdentifiers.Contains(x.ChannelIdentifier)).ToList();

            // Failed channels keep their previous state.
            PopulateLivestreams(providerResults.Where(x => x.IsSuccess).ToList());

            ProviderRefreshComplete?.Invoke(this, EventArgs.Empty);
            return providerResults;
        }

        private static List<LivestreamQueryResult> FailedChannels(IApiClient apiClient, List<ChannelIdentifier> channels, Exception exception)
        {
            var failure = new FailedQueryException(apiClient, exception);
            return channels.Select(channel => new LivestreamQueryResult(channel) { FailedQueryException = failure }).ToList();
        }

        private void SaveChangedChannelDetails(Dictionary<ChannelIdentifier, (string DisplayName, string TwitchLogin)> savedDetails)
        {
            var channelDetailsChanged = channelIdentifiers.Any(x =>
                savedDetails.TryGetValue(x, out var details) && details != (x.DisplayName, x.TwitchLogin));

            if (initialDataDirty || channelDetailsChanged) SaveLivestreams();
            initialDataDirty = false;
        }

        private void FinishRefresh()
        {
            OnOnlineLivestreamsRefreshComplete();
            lastRefreshTime = DateTimeOffset.Now;
            CanRefreshLivestreams = Livestreams.Any();
        }

        public void IgnoreQueryFailure(string errorToIgnore)
        {
            if (string.IsNullOrWhiteSpace(errorToIgnore))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(errorToIgnore));

            ignoredQueryFailures.Add(errorToIgnore);
        }

        public async Task RemoveLivestream(ChannelIdentifier channelIdentifier)
        {
            if (channelIdentifier == null) return;

            await channelIdentifier.ApiClient.RemoveChannel(channelIdentifier);
            if (channelIdentifiers.TryGetValue(channelIdentifier, out var monitoredChannel))
                monitoredChannel.TwitchLoginChanged -= ChannelTwitchLoginChanged;

            channelIdentifiers.Remove(channelIdentifier);
            // TODO - if removal of a channel would remove more than 1 livestream, consider warning the user
            var matchingLivestreams = Livestreams.Where(x => Equals(channelIdentifier, x.ChannelIdentifier)).ToList();
            foreach (var stream in matchingLivestreams) Livestreams.Remove(stream);

            SaveLivestreams();
        }

        protected virtual void OnOnlineLivestreamsRefreshComplete()
        {
            LivestreamsRefreshComplete?.Invoke(this, EventArgs.Empty);
        }

        /// <summary> Applies successful query results; only rows of the queried channels are replaced or removed. </summary>
        private void PopulateLivestreams(List<LivestreamQueryResult> successfulQueries)
        {
            var settings = settingsHandler.Settings;
            var livestreamModels = successfulQueries.Select(x => x.LivestreamModel).ToList();
            foreach (var livestream in livestreamModels)
            {
                var livestreamModel = Livestreams.FirstOrDefault(x => Equals(livestream, x));
                if (livestreamModel == null || ReferenceEquals(livestreamModel, livestream)) continue;

                var category = livestreamModel.ApplyUpdate(livestream);
                if (category == null) continue;
                if (!settings.EnableCategoryChangeNotifications && !settings.CategoryChangeNotifications.Contains(livestreamModel.UniqueStreamKey)) continue;
                if (settings.ExcludeFromNotifying.Contains(livestreamModel.UniqueStreamKey)) continue;

                CategoryChangeNotification?.Invoke(LivestreamNotification.CreateForFollowedStream(livestreamModel, category));
            }

            var newStreams = livestreamModels.Except(Livestreams).ToList();
            var refreshedChannels = new HashSet<ChannelIdentifier>(successfulQueries.Select(x => x.ChannelIdentifier));
            var removedStreams = Livestreams.Where(x => refreshedChannels.Contains(x.ChannelIdentifier)).Except(livestreamModels).ToList();

            // Added before the old rows are removed: a new broadcast notifies only while its channel still has a row.
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

            if (channelAdded) SaveLivestreams();
        }

        private void SaveLivestreams()
        {
            fileHandler.SaveToDisk(channelIdentifiers);
        }

        private void ChannelTwitchLoginChanged(object sender, EventArgs e)
        {
            // A refresh saves channel metadata once, after applying every provider's results.
            if (CanRefreshLivestreams) SaveLivestreams();
        }
    }
}
