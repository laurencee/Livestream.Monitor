using System.ComponentModel;
using System.Net;
using ExternalAPIs;
using Livestream.Monitor.Core.UI;
using Livestream.Monitor.Core;
using Livestream.Monitor.Core.Settings;
using Livestream.Monitor.Model;
using Livestream.Monitor.Model.ApiClients;
using Livestream.Monitor.Model.Monitoring;

namespace Livestream.Monitor.Direct2D.Windows.Monitor;

internal enum MonitorPage
{
    Followed,
    TopStreams,
    Vods,
}

/// <summary>The title and message of the dialog that reports a failed operation.</summary>
internal readonly record struct ErrorDialog(string Title, string Message);

/// <summary>The title and message of the dialog shown while an operation runs.</summary>
internal readonly record struct ProgressDialog(string Title, string Message);

/// <summary>
/// The main window's state and the operations that change it: rows, filters, paging, selection, status and the
/// provider queries over the Core streams model. It is kept apart from <see cref="MonitorWindow"/>, which draws from
/// it every frame, so that this logic is tested without a window, a message loop or a graphics device.
/// </summary>
internal sealed class MonitorWindowState : ObservableModel
{
    private const string RefreshErrorTitle = "Error refreshing livestreams";
    private readonly IUserDialogs dialogs;
    private readonly Func<string, string, IDisposable>? showProgress;
    private readonly Func<string, string, (IDisposable Dialog, Task Completion)>? openBrowsingError;
    private readonly List<LivestreamModel> topStreams = [];
    private readonly Dictionary<LivestreamModel, string> displayedUptimes = [];
    private readonly HashSet<UniqueStreamKey> followedKeys = [];
    private bool followedKeysStale = true;
    private long nextRefresh;
    private int failedAutomaticRefreshes;
    private int gameSuggestionGeneration;
    private int contentQueryGeneration;
    private IDisposable? browsingError;

    public MonitorWindowState(
        IUserDialogs dialogs,
        ISettingsHandler? settingsHandler = null,
        IApiClientFactory? apiClientFactory = null,
        IMonitorStreamsModel? monitorStreamsModel = null,
        Func<string, string, IDisposable>? showProgress = null,
        Func<string, string, (IDisposable Dialog, Task Completion)>? openBrowsingError = null)
    {
        this.dialogs = dialogs;
        this.showProgress = showProgress;
        this.openBrowsingError = openBrowsingError;
        SettingsHandler = settingsHandler ?? new SettingsHandler();
        OnlineOnly = SettingsHandler.Settings.ShowOnlineOnly;
        ApiClientFactory = apiClientFactory ?? new ApiClientFactory(SettingsHandler);
        var fileHandler = new MonitoredStreamsFileHandler(ApiClientFactory);
        StreamsModel = monitorStreamsModel ?? new MonitorStreamsModel(fileHandler, SettingsHandler, ApiClientFactory);
        StreamsModel.Livestreams.CollectionChanged += (_, args) =>
        {
            foreach (LivestreamModel stream in args.NewItems ?? Array.Empty<LivestreamModel>())
            {
                stream.PropertyChanged += OnStreamChanged;
                displayedUptimes[stream] = stream.Uptime.ToTotalHoursText();
            }

            foreach (LivestreamModel stream in args.OldItems ?? Array.Empty<LivestreamModel>())
            {
                stream.PropertyChanged -= OnStreamChanged;
                displayedUptimes.Remove(stream);
            }

            followedKeysStale = true;
            Changed?.Invoke();
        };

        StreamsModel.ProviderRefreshComplete += (_, _) => UpdateDisplayedUptimes();
        StreamsModel.LivestreamsRefreshComplete += (_, _) => UpdateDisplayedUptimes();

        SettingsHandler.Settings.PropertyChanged += (_, _) => Changed?.Invoke();
    }

    public ISettingsHandler SettingsHandler { get; }
    public IApiClientFactory ApiClientFactory { get; }
    public IMonitorStreamsModel StreamsModel { get; }
    public List<LivestreamModel> Rows { get; } = [];
    public List<VodDetails> Vods { get; } = [];

    public MonitorPage Page
    {
        get;
        set
        {
            // WPF's list restarts its refresh failure count each time it is shown.
            if (value == MonitorPage.Followed && field != MonitorPage.Followed) failedAutomaticRefreshes = 0;
            field = value;
            InvalidateGameSuggestions();
            InvalidateContentQuery();
        }
    }

    public string Filter { get; set; } = "";
    public string ProviderFilter { get; set; } = FilterModel.AllApiClientsFilterName;
    public bool OnlineOnly { get; set; }
    public int SelectedIndex { get; set; } = -1;
    public int PageNumber { get; set; }
    public int PageSize { get; set; } = 15;
    public bool HasNextPage { get; private set; }
    public bool Busy { get; private set => Set(ref field, value); }
    public bool IsContentLoading { get; private set; } = true;
    internal bool FollowedRowsSettled => StreamsModel.CanRefreshLivestreams || StreamsModel.Livestreams.Count == 0;
    public string Status { get; private set => Set(ref field, value); } = "Initializing…";
    public LivestreamModel? SelectedStream =>
        SelectedIndex >= 0 && SelectedIndex < Rows.Count ? Rows[SelectedIndex] : null;
    public event Action? Changed;
    public event Action<LivestreamModel>? StreamOnline;

    public string GetDisplayedUptime(LivestreamModel stream)
    {
        if (!stream.Live) return TimeSpan.Zero.ToTotalHoursText();
        if (displayedUptimes.TryGetValue(stream, out var uptime)) return uptime;

        uptime = stream.Uptime.ToTotalHoursText();
        displayedUptimes.Add(stream, uptime);
        return uptime;
    }

    /// <summary>True when a stream with the same key is monitored, for example a followed top stream.</summary>
    public bool IsFollowed(LivestreamModel stream)
    {
        if (followedKeysStale)
        {
            followedKeys.Clear();
            foreach (var followed in StreamsModel.Livestreams) followedKeys.Add(followed.UniqueStreamKey);

            followedKeysStale = false;
        }

        return followedKeys.Contains(stream.UniqueStreamKey);
    }

    /// <summary>
    /// Starts loading and querying the followed channels in the background; called before the window exists so the
    /// requests start as early as possible. Later calls do nothing.
    /// </summary>
    public void BeginInitialQuery()
    {
        if (StreamsModel is MonitorStreamsModel monitor) monitor.BeginInitialQuery();
    }

    /// <summary>
    /// Loads followed channels and starts the first refresh. Shows rows after the first provider's results are applied,
    /// but stays busy until every provider's first results are applied.
    /// </summary>
    public async Task Initialize()
    {
        await RunContentQuery(DescribeError, async () =>
        {
            var progress = showProgress?.Invoke("Initializing", "Initializing App...");
            StreamsModel.ProviderRefreshComplete += ShowRows;
            try
            {
                BeginInitialQuery();
                await StreamsModel.Initialize(dialogs);
                ScheduleRefresh();
            }
            finally
            {
                StreamsModel.ProviderRefreshComplete -= ShowRows;
                // Close the progress dialog before showing an error.
                CloseProgress();
            }

            void ShowRows(object? sender, EventArgs args)
            {
                StreamsModel.ProviderRefreshComplete -= ShowRows;
                CloseProgress();
                IsContentLoading = false;
                Changed?.Invoke();
            }

            void CloseProgress()
            {
                // ReSharper disable once AccessToModifiedClosure -- clearing progress prevents the finally block from disposing it twice
                progress?.Dispose();
                progress = null;
            }
        });

        static ErrorDialog DescribeError(Exception exception) =>
            new("Error", $"An error occurred initializing the app.{Environment.NewLine}{exception.ExtractErrorMessage()}");
    }

    public void Tick()
    {
        // Rescheduled before refreshing, so a failed refresh waits a full period instead of retrying every frame.
        if (!Busy && StreamsModel.Initialised && Environment.TickCount64 >= nextRefresh)
        {
            ScheduleRefresh();
            _ = Refresh(automatic: true);
        }
    }

    /// <summary>
    /// Refreshes the followed channels. As WPF's refresh button does, a requested refresh reports every failure; as WPF's
    /// list does, an automatic one reports nothing off the followed page, and reports failed provider queries only from
    /// the third failed refresh in a row there, unless refresh error dialogs are disabled. Each reported failure can be
    /// ignored for the rest of the session.
    /// </summary>
    public async Task Refresh(bool automatic = false)
    {
        await RunAndReport(ReportFailure, progress: null, async () =>
        {
            await StreamsModel.RefreshLivestreams();
            ScheduleRefresh();
            if (automatic) failedAutomaticRefreshes = 0;
        });

        // As WPF restarts its refresh timer after the dialogs close, the next automatic refresh waits a full period.
        if (automatic) ScheduleRefresh();

        // WPF's list counts the refreshes that coincide with initialization as its first, so its first failure that
        // opens dialogs is the third failed refresh in a row.
        async Task ReportFailure(Exception exception)
        {
            // WPF's list stops refreshing off its page; its count restarts when the page is shown again.
            if (automatic && Page != MonitorPage.Followed) return;

            if (automatic) failedAutomaticRefreshes++;

            if (exception is not AggregateException aggregate)
            {
                await dialogs.ShowMessage(RefreshErrorTitle, exception.ExtractErrorMessage());
                return;
            }

            const int ReportedFailureCount = 3;
            var quiet = failedAutomaticRefreshes < ReportedFailureCount;
            if (automatic && (quiet || SettingsHandler.Settings.DisableRefreshErrorDialogs)) return;

            foreach (var failure in aggregate.InnerExceptions)
            {
                var message = failure.ExtractErrorMessage();
                if (!await dialogs.Confirm(RefreshErrorTitle, message, negative: "Ignore")) StreamsModel.IgnoreQueryFailure(message);
            }
        }
    }

    private void ScheduleRefresh() =>
        nextRefresh = Environment.TickCount64 + (long)Constants.RefreshPollingTime.TotalMilliseconds;

    public void RebuildRows()
    {
        if (Page == MonitorPage.Vods)
        {
            Rows.Clear();
            if (SelectedIndex >= Vods.Count) SelectedIndex = -1;
            return;
        }

        var selected = SelectedStream;

        if (Page == MonitorPage.TopStreams)
        {
            Rows.Clear();
            Rows.AddRange(topStreams);
            SelectedIndex = selected == null ? -1 : Rows.IndexOf(selected);
            return;
        }

        var filtered = StreamsModel.Livestreams.Where(stream =>
            FilterModel.Matches(stream, Filter, ProviderFilter, OnlineOnly));

        var sort = SettingsHandler.Settings.LivestreamListSortState;
        var descending = sort?.SortDirection != ListSortDirection.Ascending;
        var ordered = sort?.Column switch
        {
            nameof(LivestreamModel.DisplayName) => Order(s => s.DisplayName),
            nameof(LivestreamModel.Game) => Order(s => s.Game),
            nameof(LivestreamModel.Description) => Order(s => s.Description),
            nameof(LivestreamModel.Uptime) => Order(s => s.Uptime),
            _ => descending
                ? filtered.OrderByDescending(s => s.Live).ThenByDescending(s => s.Viewers)
                : filtered.OrderBy(s => s.Live).ThenBy(s => s.Viewers),
        };

        Rows.Clear();
        Rows.AddRange(ordered);
        SelectedIndex = selected == null ? -1 : Rows.IndexOf(selected);

        IOrderedEnumerable<LivestreamModel> Order<TKey>(Func<LivestreamModel, TKey> key) =>
            descending ? filtered.OrderByDescending(key) : filtered.OrderBy(key);
    }

    /// <summary>Adds a channel to monitor, returning whether it was added.</summary>
    public async Task<bool> Add(string name, IApiClient api)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;

        var channel = name.Trim();
        var progress = new ProgressDialog("Adding channel", $"Adding channel '{channel}'");
        return await Run(DescribeError, () => StreamsModel.AddLivestream(new ChannelIdentifier(api, channel), dialogs), progress);

        ErrorDialog DescribeError(Exception exception)
        {
            const string Tip =
                "Tip: Make sure you have selected the right platform\nTip: Enter a channel name or ID, not a URL.";

            var notFound = exception.InnerException is HttpRequestWithStatusException { StatusCode: HttpStatusCode.NotFound };
            var message = notFound
                ? $"No channel found named '{channel}' for platform {PlatformNames.GetDisplayName(api.ApiName)}{Environment.NewLine}{Environment.NewLine}{Tip}"
                : $"{exception.ExtractErrorMessage()}{Environment.NewLine}{Tip}";

            return new ErrorDialog("Error adding stream", message);
        }
    }

    public async Task Import(string name, IApiClient api)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        if (!api.HasFollowedChannelsQuerySupport) return;

        var username = name.Trim();
        var message = $"Importing followed channels from '{PlatformNames.GetDisplayName(api.ApiName)}' for username '{username}'";
        var progress = new ProgressDialog("Importing followed channels", message);
        await Run(DescribeError, () => StreamsModel.ImportFollows(username, api, dialogs), progress);

        static ErrorDialog DescribeError(Exception exception) => new("Error importing channels", exception.ExtractErrorMessage());
    }

    public async Task Remove(LivestreamModel stream)
    {
        if (!await dialogs.ConfirmRemoval(stream)) return;

        await Run("Error Removing Stream", () => StreamsModel.RemoveOrReport(stream, dialogs));
    }

    public void InvalidateGameSuggestions() => gameSuggestionGeneration++;

    public void InvalidateContentQuery() => contentQueryGeneration++;

    public void AbandonBrowsingRequest()
    {
        InvalidateContentQuery();

        if (browsingError == null) return;

        Status = "";
        browsingError.Dispose();
    }

    public async Task QueryGameSuggestions(IApiClient api, string filter, Action<IReadOnlyList<KnownGame>> publish)
    {
        // Page, provider and filter values may match again by the time an old request completes.
        var generation = ++gameSuggestionGeneration;

        var games = await api.GetKnownGameNames(filter);

        if (generation == gameSuggestionGeneration && Page == MonitorPage.TopStreams) publish(games);
    }

    public async Task QueryTop(IApiClient api, string category)
    {
        InvalidateGameSuggestions();

        if (!api.HasTopStreamsSupport)
        {
            topStreams.Clear();
            HasNextPage = false;
            Status = $"{PlatformNames.GetDisplayName(api.ApiName)} does not support Top Streams.";
            Changed?.Invoke();
            return;
        }

        var generation = contentQueryGeneration;
        await RunContentQuery(DescribeError, Load, IsCurrent);

        bool IsCurrent() => generation == contentQueryGeneration;

        async Task Load()
        {
            topStreams.Clear();
            HasNextPage = false;

            if (!api.IsAuthorized) await api.Authorize(dialogs);
            if (!IsCurrent()) return;

            var query = new TopStreamQuery
            {
                Skip = PageNumber * PageSize,
                Take = PageSize,
                GameName = api.HasTopStreamGameFilterSupport ? category : null,
            };

            var result = await api.GetTopStreams(query);
            if (!IsCurrent()) return;

            topStreams.AddRange(result.LivestreamModels);
            HasNextPage = result.HasNextPage;
        }

        ErrorDialog DescribeError(Exception exception)
        {
            var message = $"Error getting top streams from '{PlatformNames.GetDisplayName(api.ApiName)}'.{Environment.NewLine}{Environment.NewLine}" +
                          exception.ExtractErrorMessage();

            return new ErrorDialog("Error", message);
        }
    }

    public async Task QueryVods(IApiClient api, string name, string type)
    {
        if (string.IsNullOrWhiteSpace(name) || !api.HasVodViewerSupport)
        {
            Vods.Clear();
            HasNextPage = false;
            Status = api.HasVodViewerSupport
                ? "Enter or select a channel to browse VODs."
                : $"{PlatformNames.GetDisplayName(api.ApiName)} does not support VODs.";

            Changed?.Invoke();
            return;
        }

        var generation = contentQueryGeneration;
        await RunContentQuery(DescribeError, Load, IsCurrent);

        bool IsCurrent() => generation == contentQueryGeneration;

        async Task Load()
        {
            Vods.Clear();
            HasNextPage = false;

            if (!api.IsAuthorized) await api.Authorize(dialogs);
            if (!IsCurrent()) return;

            var query = new VodQuery
            {
                StreamDisplayName = name,
                Skip = PageNumber * PageSize,
                Take = PageSize,
                VodTypes = [type],
            };

            var result = await api.GetVods(query);
            if (!IsCurrent()) return;

            Vods.AddRange(result);
            HasNextPage = Vods.Count == PageSize;
        }

        ErrorDialog DescribeError(Exception exception)
        {
            if (exception is HttpRequestWithStatusException { StatusCode: HttpStatusCode.NotFound })
                return new ErrorDialog("Error", $"Unknown channel '{name}'.");

            var message = $"Could not load VODs from '{PlatformNames.GetDisplayName(api.ApiName)}' for channel '{name}'." +
                          $"{Environment.NewLine}{Environment.NewLine}{exception.ExtractErrorMessage()}";

            return new ErrorDialog("Could not load VODs", message);
        }
    }

    /// <summary>
    /// Runs the action unless another is running, returning whether it ran and succeeded. A failure is shown under the
    /// error title, which names what was being done as the WPF frontend's titles do.
    /// </summary>
    public Task<bool> Run(string errorTitle, Func<Task> action) =>
        Run(exception => new ErrorDialog(errorTitle, exception.ExtractErrorMessage()), action);

    /// <summary>
    /// Runs the action unless another is running, returning whether it ran and succeeded; a failure is shown as described.
    /// A progress dialog, if given and the state was created with a way to show one, is shown while the action runs and
    /// closed before any failure is shown.
    /// </summary>
    private Task<bool> Run(
        Func<Exception, ErrorDialog> describeError,
        Func<Task> action,
        ProgressDialog? progress = null,
        Func<bool>? isCurrentBrowsingRequest = null)
    {
        return RunAndReport(ShowError, progress, action, shouldReportError: isCurrentBrowsingRequest);

        async Task ShowError(Exception exception)
        {
            var error = describeError(exception);
            if (isCurrentBrowsingRequest == null || openBrowsingError == null)
            {
                await dialogs.ShowMessage(error.Title, error.Message);
                return;
            }

            var opened = openBrowsingError(error.Title, error.Message);
            using var dialog = opened.Dialog;
            browsingError = dialog;
            try
            {
                await opened.Completion;
            }
            finally
            {
                browsingError = null;
            }
        }
    }

    private async Task<bool> RunAndReport(
        Func<Exception, Task> reportError,
        ProgressDialog? progress,
        Func<Task> action,
        Func<bool>? shouldReportError = null)
    {
        if (Busy) return false;

        Busy = true;
        Status = "Loading…";
        Changed?.Invoke();
        try
        {
            using (progress is { } shown ? showProgress?.Invoke(shown.Title, shown.Message) : null) await action();

            Status = $"Updated {DateTime.Now:t} · {StreamsModel.Livestreams.Count} monitored channels";
            return true;
        }
        catch (Exception exception)
        {
            if (shouldReportError?.Invoke() == false)
            {
                Status = "";
                return false;
            }

            // The footer has room for one line; the dialog shows each failure on its own line.
            Status = exception.ExtractErrorMessage().ReplaceLineEndings(" · ");
            await reportError(exception);
            return false;
        }
        finally
        {
            Busy = false;
            Changed?.Invoke();
        }
    }

    private async Task RunContentQuery(Func<Exception, ErrorDialog> describeError, Func<Task> action, Func<bool>? isCurrent = null)
    {
        if (Busy) return;

        IsContentLoading = true;
        try
        {
            await Run(describeError, action, isCurrentBrowsingRequest: isCurrent);
        }
        finally
        {
            IsContentLoading = false;
            Changed?.Invoke();
        }
    }

    private void UpdateDisplayedUptimes()
    {
        foreach (var stream in StreamsModel.Livestreams)
            displayedUptimes[stream] = stream.Uptime.ToTotalHoursText();

        Changed?.Invoke();
    }

    private void OnStreamChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(LivestreamModel.Live) && sender is LivestreamModel { Live: true } stream &&
            StreamsModel.Initialised && !stream.DontNotify && !SettingsHandler.Settings.DisableNotifications)
        {
            StreamOnline?.Invoke(stream);
        }

        Changed?.Invoke();
    }
}
