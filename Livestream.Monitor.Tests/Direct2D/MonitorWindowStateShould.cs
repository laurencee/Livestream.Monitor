using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Livestream.Monitor.Core;
using Livestream.Monitor.Core.Settings;
using Livestream.Monitor.Direct2D.Windows.Monitor;
using Livestream.Monitor.Model;
using Livestream.Monitor.Model.ApiClients;
using Livestream.Monitor.Model.Monitoring;
using NSubstitute;
using Xunit;

namespace Livestream.Monitor.Tests.Direct2D;

public class MonitorWindowStateShould
{
    private const string RefreshErrorTitle = "Error refreshing livestreams";

    // The startup progress dialog opens only if no rows have arrived after a second, and never once rows are showing.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShowRowsOnceTheFirstProviderRefreshesWhileStayingBusyUntilInitializationCompletes(bool slowStart)
    {
        var progressShown = false;
        var progressClosed = false;
        var clock = new StartupClock();
        var startupProgressDelay = TimeSpan.FromSeconds(1);
        var model = Create(showProgress: ShowProgress, timeProvider: clock);
        var pending = new TaskCompletionSource();
        model.StreamsModel.Initialize(Arg.Any<IUserDialogs>(), Arg.Any<CancellationToken>()).Returns(pending.Task);

        model.StreamsModel.Livestreams.Add(Stream("offline", "Channel", "youtube", online: false, viewers: 0));

        var initialization = model.Initialize();
        model.Tick();

        Assert.False(progressShown);
        Assert.True(model.IsContentLoading);
        Assert.False(model.CanOpenContextMenu);

        if (slowStart)
        {
            clock.Advance(startupProgressDelay);
            model.Tick();

            Assert.True(progressShown);
            Assert.False(progressClosed);
        }

        model.StreamsModel.ProviderRefreshComplete += Raise.Event<EventHandler>();
        clock.Advance(startupProgressDelay);
        model.Tick();

        Assert.Equal(slowStart, progressShown);
        Assert.Equal(slowStart, progressClosed);
        Assert.False(model.IsContentLoading);
        Assert.True(model.Busy);

        model.RebuildRows();
        model.SelectedIndex = 0;

        Assert.False(model.CanOpenContextMenu);
        Assert.NotNull(model.SelectedStream);

        model.SelectedStream.Live = true;

        Assert.True(model.CanOpenContextMenu);

        pending.SetResult();
        await initialization;

        Assert.False(model.Busy);
        Assert.False(model.FollowedRowsSettled);

        model.StreamsModel.CanRefreshLivestreams.Returns(true);

        Assert.True(model.FollowedRowsSettled);

        IDisposable ShowProgress(string title, string message)
        {
            progressShown = true;
            var progress = Substitute.For<IDisposable>();
            progress.When(x => x.Dispose()).Do(_ => progressClosed = true);
            return progress;
        }
    }

    [Fact]
    public async Task ClearOldBrowsingResultsWhenARequestFailsOrIsUnsupported()
    {
        var model = Create();
        model.Page = MonitorPage.TopStreams;
        var api = AuthorizedProvider(topStreams: true);
        var firstPage = new TopStreamsResponse
        {
            LivestreamModels = [Stream("first", "First", "twitchtv", online: true, viewers: 10)],
            HasNextPage = true,
        };
        var failure = new InvalidOperationException("Controlled provider failure");
        api.GetTopStreams(Arg.Any<ExternalAPIs.TopStreamQuery>())
            .Returns(Task.FromResult(firstPage), Task.FromResult(firstPage), Task.FromException<TopStreamsResponse>(failure));
        await model.QueryTop(api, "");
        model.RebuildRows();
        Assert.Single(model.Rows);

        // A provider without top streams.
        await model.QueryTop(Substitute.For<IApiClient>(), "");
        model.RebuildRows();

        Assert.Empty(model.Rows);
        Assert.False(model.HasNextPage);

        await model.QueryTop(api, "");
        model.PageNumber = 1;
        await model.QueryTop(api, "");
        model.RebuildRows();

        Assert.Empty(model.Rows);
        Assert.False(model.HasNextPage);
        Assert.False(model.BrowsingResultIsEmpty);

        model.Page = MonitorPage.Vods;
        var vodApi = AuthorizedProvider(vods: true);
        IReadOnlyCollection<VodDetails> firstChannel = [new() { Title = "Old VOD" }];
        vodApi.GetVods(Arg.Any<VodQuery>())
            .Returns(Task.FromResult(firstChannel), Task.FromException<IReadOnlyCollection<VodDetails>>(failure));
        await model.QueryVods(vodApi, "old", "archive");
        Assert.Single(model.Vods);

        await model.QueryVods(vodApi, "new", "archive");

        Assert.Empty(model.Vods);
        Assert.False(model.IsContentLoading);
        Assert.False(model.Busy);
        Assert.False(model.BrowsingResultIsEmpty);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task SuppressSupersededBrowsingErrors(bool browseVods, bool replaceOnSamePage)
    {
        const string OldError = "Obsolete provider failure";
        var dialogs = Substitute.For<IUserDialogs>();
        var model = Create(dialogs);
        var page = browseVods ? MonitorPage.Vods : MonitorPage.TopStreams;
        model.Page = page;
        var api = AuthorizedProvider(topStreams: !browseVods, vods: browseVods);
        var vods = new TaskCompletionSource<IReadOnlyCollection<VodDetails>>();
        var top = new TaskCompletionSource<TopStreamsResponse>();
        api.GetVods(Arg.Any<VodQuery>()).Returns(vods.Task);
        api.GetTopStreams(Arg.Any<ExternalAPIs.TopStreamQuery>()).Returns(top.Task);
        var query = browseVods ? model.QueryVods(api, "channel", "archive") : model.QueryTop(api, "");
        if (replaceOnSamePage)
        {
            model.InvalidateContentQuery();
        }
        else
        {
            model.Page = MonitorPage.Followed;
            model.Page = page;
        }

        if (browseVods) vods.SetException(new InvalidOperationException(OldError));
        else top.SetException(new InvalidOperationException(OldError));
        await query;

        await dialogs.DidNotReceive().ShowMessage(Arg.Any<string>(), Arg.Any<string>());
        Assert.Empty(model.Status);
        Assert.False(model.Busy);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DismissAnOpenBrowsingErrorWhenAbandoningTheRequest(bool browseVods)
    {
        var dialog = Substitute.For<IDisposable>();
        var completion = new TaskCompletionSource();
        dialog.When(pane => pane.Dispose()).Do(_ => completion.TrySetResult());
        var model = Create(openBrowsingError: (_, _) => (dialog, completion.Task));
        var provider = AuthorizedProvider(topStreams: !browseVods, vods: browseVods);
        var failure = new InvalidOperationException("Controlled browsing error");
        provider.GetTopStreams(Arg.Any<ExternalAPIs.TopStreamQuery>()).Returns(Task.FromException<TopStreamsResponse>(failure));
        provider.GetVods(Arg.Any<VodQuery>()).Returns(Task.FromException<IReadOnlyCollection<VodDetails>>(failure));

        model.Page = browseVods ? MonitorPage.Vods : MonitorPage.TopStreams;
        var query = browseVods ? model.QueryVods(provider, "channel", "archive") : model.QueryTop(provider, "");

        try
        {
            Assert.True(model.Busy);
            model.AbandonBrowsingRequest();

            Assert.True(completion.Task.IsCompleted);
            await query;
            Assert.False(model.Busy);
            Assert.False(model.IsContentLoading);
            Assert.Empty(model.Status);
        }
        finally
        {
            completion.TrySetResult();
            await query;
        }
    }

    [Fact]
    public async Task LeaveAnUnrelatedErrorOpenWhenAbandoningBrowsing()
    {
        var dialogs = Substitute.For<IUserDialogs>();
        var completion = new TaskCompletionSource();
        dialogs.ShowMessage(Arg.Any<string>(), Arg.Any<string>()).Returns(completion.Task);
        var model = Create(
            dialogs,
            openBrowsingError: (_, _) => throw new InvalidOperationException("Expected the ordinary error dialog."));
        var failure = new InvalidOperationException("Controlled failure");
        var operation = model.Run("Error", () => Task.FromException(failure));

        try
        {
            model.AbandonBrowsingRequest();

            Assert.True(model.Busy);
            Assert.False(operation.IsCompleted);
        }
        finally
        {
            completion.TrySetResult();
            await operation;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SkipTheProviderFetchWhenBrowsingIsAbandonedDuringAuthorization(bool browseVods)
    {
        var model = Create();
        var provider = AuthorizedProvider(topStreams: !browseVods, vods: browseVods);
        var authorization = new TaskCompletionSource();
        provider.IsAuthorized.Returns(false);
        provider.Authorize(Arg.Any<IUserDialogs>()).Returns(authorization.Task);
        provider.GetTopStreams(Arg.Any<ExternalAPIs.TopStreamQuery>()).Returns(new TopStreamsResponse());
        provider.GetVods(Arg.Any<VodQuery>()).Returns(Array.Empty<VodDetails>());

        model.Page = browseVods ? MonitorPage.Vods : MonitorPage.TopStreams;
        var query = browseVods ? model.QueryVods(provider, "channel", "archive") : model.QueryTop(provider, "");
        model.AbandonBrowsingRequest();
        authorization.SetResult();
        await query;

        await provider.DidNotReceive().GetTopStreams(Arg.Any<ExternalAPIs.TopStreamQuery>());
        await provider.DidNotReceive().GetVods(Arg.Any<VodQuery>());
        Assert.False(model.Busy);
    }

    [Fact]
    public async Task DiscardVodResultsWhenTheChannelChangesBeforeTheNextQueryStarts()
    {
        var model = Create();
        model.Page = MonitorPage.Vods;
        var api = AuthorizedProvider(vods: true);
        var pending = new TaskCompletionSource<IReadOnlyCollection<VodDetails>>();
        api.GetVods(Arg.Any<VodQuery>()).Returns(pending.Task);

        var query = model.QueryVods(api, "old", "archive");
        model.InvalidateContentQuery();
        pending.SetResult([new VodDetails { Title = "Old VOD" }]);
        await query;

        Assert.Empty(model.Vods);
    }

    [Fact]
    public async Task CloseTheProgressDialogBeforeShowingAFailure()
    {
        var events = new List<string>();
        var dialogs = Substitute.For<IUserDialogs>();
        dialogs.ShowMessage(Arg.Any<string>(), Arg.Any<string>()).Returns(_ =>
        {
            events.Add("error shown");
            return Task.CompletedTask;
        });

        // Disabling refresh error dialogs does not hide other failures.
        var model = Create(dialogs, ShowProgress);
        model.SettingsHandler.Settings.DisableRefreshErrorDialogs = true;
        var failure = Task.FromException(new InvalidOperationException("Controlled provider failure"));
        model.StreamsModel.AddLivestream(Arg.Any<ChannelIdentifier>(), Arg.Any<IUserDialogs>()).Returns(failure, Task.CompletedTask);

        // The window clears the add input only after a successful add.
        Assert.False(await model.Add("example", Substitute.For<IApiClient>()));

        Assert.Equal(["progress shown", "progress closed", "error shown"], events);
        await dialogs.Received(1).ShowMessage("Error adding stream", Arg.Any<string>());
        Assert.False(model.Busy);

        Assert.True(await model.Add("example", Substitute.For<IApiClient>()));
        Assert.False(model.Busy);

        IDisposable ShowProgress(string title, string message)
        {
            events.Add("progress shown");
            var progress = Substitute.For<IDisposable>();
            progress.When(x => x.Dispose()).Do(_ => events.Add("progress closed"));
            return progress;
        }
    }

    [Fact]
    public async Task ReportAutomaticRefreshFailuresFromTheThirdInARow()
    {
        var dialogs = Substitute.For<IUserDialogs>();
        var model = Create(dialogs);
        await model.Initialize();
        model.StreamsModel.RefreshLivestreams().Returns(_ => Task.FromException(ProviderFailures()));
        int[] expectedReports = [0, 0, 1, 2];

        foreach (var expected in expectedReports)
        {
            await model.Refresh(automatic: true);
            await dialogs.Received(expected).Confirm(RefreshErrorTitle, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
        }

        // A successful refresh keeps the table visible while it runs and restarts the count.
        var pending = new TaskCompletionSource();
        model.StreamsModel.RefreshLivestreams().Returns(pending.Task);
        var refresh = model.Refresh(automatic: true);
        Assert.False(model.IsContentLoading);
        pending.SetResult();
        await refresh;

        model.StreamsModel.RefreshLivestreams().Returns(_ => Task.FromException(ProviderFailures()));
        for (var i = 0; i < 2; i++) await model.Refresh(automatic: true);
        const int ReportsSoFar = 2;
        await dialogs.Received(ReportsSoFar).Confirm(RefreshErrorTitle, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());

        // Disabled refresh error dialogs hide only the automatic failures.
        model.SettingsHandler.Settings.DisableRefreshErrorDialogs = true;
        await model.Refresh(automatic: true);
        await dialogs.Received(ReportsSoFar).Confirm(RefreshErrorTitle, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());

        await model.Refresh();
        await dialogs.Received(ReportsSoFar + 1).Confirm(RefreshErrorTitle, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());

        // An unanswered report does not hold up the automatic refreshes after it, and their failures are not reported on top of it.
        model.SettingsHandler.Settings.DisableRefreshErrorDialogs = false;
        var answer = new TaskCompletionSource<bool>();
        dialogs.Confirm(RefreshErrorTitle, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(answer.Task);
        var unanswered = model.Refresh(automatic: true);
        Assert.False(model.Busy);

        model.StreamsModel.ClearReceivedCalls();
        await model.Refresh(automatic: true);
        await model.StreamsModel.Received(1).RefreshLivestreams();
        await dialogs.Received(ReportsSoFar + 2).Confirm(RefreshErrorTitle, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());

        answer.SetResult(true);
        await unanswered;
    }

    [Fact]
    public async Task RestartTheAutomaticRefreshFailureCountWhenTheFollowedPageIsShownAgain()
    {
        var dialogs = Substitute.For<IUserDialogs>();
        var model = Create(dialogs);
        model.StreamsModel.RefreshLivestreams().Returns(_ => Task.FromException(ProviderFailures()));

        for (var i = 0; i < 2; i++) await model.Refresh(automatic: true);

        // Off the followed page no automatic failure is reported.
        foreach (var page in new[] { MonitorPage.TopStreams, MonitorPage.Vods })
        {
            model.Page = page;
            for (var i = 0; i < 3; i++) await model.Refresh(automatic: true);
        }

        await dialogs.DidNotReceive().Confirm(RefreshErrorTitle, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
        await dialogs.DidNotReceiveWithAnyArgs().ShowMessage(null, null);

        model.Page = MonitorPage.Followed;
        for (var i = 0; i < 2; i++) await model.Refresh(automatic: true);
        await dialogs.DidNotReceive().Confirm(RefreshErrorTitle, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());

        await model.Refresh(automatic: true);
        await dialogs.Received(1).Confirm(RefreshErrorTitle, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IgnoreARefreshFailureOnlyWhenTheUserChoosesIgnore(bool acknowledged)
    {
        const string Failure = "Provider failed.";
        var dialogs = Substitute.For<IUserDialogs>();
        dialogs.Confirm(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(acknowledged);
        var model = Create(dialogs);
        model.StreamsModel.RefreshLivestreams().Returns(_ => Task.FromException(ProviderFailures()));

        await model.Refresh();

        model.StreamsModel.Received(acknowledged ? 0 : 1).IgnoreQueryFailure(Failure);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShowZeroOfflineUptimeEvenAfterDisplayingAnOnlineValue(bool previouslyOnline)
    {
        var model = Create();
        var stream = Stream(id: "one", name: "Example", provider: "twitchtv", previouslyOnline, viewers: 10);
        stream.StartTime = DateTimeOffset.Now.AddHours(-1);
        model.StreamsModel.Livestreams.Add(stream);
        if (previouslyOnline) Assert.NotEmpty(model.GetDisplayedUptime(stream));

        stream.Live = false;
        Assert.Equal("0", model.GetDisplayedUptime(stream));
        model.StreamsModel.LivestreamsRefreshComplete += Raise.Event<EventHandler>();
        Assert.Equal("0", model.GetDisplayedUptime(stream));
    }

    [Fact]
    public void KeepDisplayedUptimeUntilStreamsRefreshEvenWhenRowsAreRebuilt()
    {
        var model = Create();
        var stream = Stream(id: "one", name: "Example", provider: "twitchtv", online: true, viewers: 10);
        stream.StartTime = DateTimeOffset.Now.AddHours(-1);
        model.StreamsModel.Livestreams.Add(stream);
        var initial = model.GetDisplayedUptime(stream);

        stream.StartTime = DateTimeOffset.Now.AddHours(-2);
        model.Filter = "Example";
        model.RebuildRows();
        Assert.Equal(initial, model.GetDisplayedUptime(stream));

        model.StreamsModel.LivestreamsRefreshComplete += Raise.Event<EventHandler>();
        Assert.StartsWith("02h ", model.GetDisplayedUptime(stream));

        const int OverOneDaySeconds = 93784;
        Assert.Equal("26h 03m 04s", TimeSpan.FromSeconds(OverOneDaySeconds).ToTotalHoursText());
    }

    // YouTube adds a broadcast as a new row in place of its channel's offline row.
    [Fact]
    public void NotifyWhenAFollowedChannelGainsALiveRowAfterTheFirstRefresh()
    {
        var settings = new Settings();
        var model = Create(savedSettings: settings);
        var provider = Substitute.For<IApiClient>();
        provider.ApiName.Returns("youtube");
        var liveAtStartup = new ChannelIdentifier(provider, "live at startup");
        var channel = new ChannelIdentifier(provider, "channel");
        var muted = new ChannelIdentifier(provider, "muted");
        var mutedOffline = new LivestreamModel("@muted", muted);
        var rows = model.StreamsModel.Livestreams;
        rows.Add(new LivestreamModel("@startup", liveAtStartup));
        rows.Add(new LivestreamModel("@channel", channel));
        rows.Add(mutedOffline);
        settings.ToggleStreamMute(mutedOffline.UniqueStreamKey);
        var notified = new List<LivestreamModel>();
        model.StreamOnline += notified.Add;

        // The startup queries find broadcasts that were already live.
        rows.Add(Broadcast("startup video", liveAtStartup));
        model.StreamsModel.LivestreamsRefreshComplete += Raise.Event<EventHandler>();
        Assert.Empty(notified);

        // Afterwards only a followed, unmuted channel's new broadcast notifies; a channel followed while live does not.
        var video = Broadcast("video", channel);
        rows.Add(video);
        rows.Add(Broadcast("muted video", muted));
        rows.Add(Broadcast("new video", new ChannelIdentifier(provider, "new")));
        Assert.Equal([video], notified);

        static LivestreamModel Broadcast(string id, ChannelIdentifier channel) => new(id, channel) { Live = true };
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnmuteAStreamFromFreshTopResults(bool duplicateSavedExclusion)
    {
        var settings = new Settings();
        var state = Create(savedSettings: settings);
        var followed = Stream("channel", "Channel", "twitchtv", online: true, viewers: 5);
        state.StreamsModel.Livestreams.Add(followed);
        settings.ToggleStreamMute(followed.UniqueStreamKey);
        Assert.Equal(followed.UniqueStreamKey, Assert.Single(settings.ExcludeFromNotifying));
        if (duplicateSavedExclusion) settings.ExcludeFromNotifying.Add(followed.UniqueStreamKey);

        var result = Stream("channel", "Channel", "twitchtv", online: true, viewers: 5);
        var provider = AuthorizedProvider(topStreams: true);
        provider.GetTopStreams(Arg.Any<ExternalAPIs.TopStreamQuery>()).Returns(new TopStreamsResponse { LivestreamModels = [result] });
        state.Page = MonitorPage.TopStreams;
        await state.QueryTop(provider, "");
        state.RebuildRows();
        settings.ToggleStreamMute(Assert.Single(state.Rows).UniqueStreamKey);

        Assert.Empty(settings.ExcludeFromNotifying);
    }

    [Fact]
    public async Task CombineNameProviderAndOnlineFilters()
    {
        var settings = new Settings { ShowOnlineOnly = true };
        var model = Create(savedSettings: settings);
        var streams = model.StreamsModel.Livestreams;
        streams.Add(Stream(id: "one", name: "Example", provider: "twitchtv", online: true, viewers: 5));
        streams.Add(Stream(id: "two", name: "Example other", provider: "youtube", online: true, viewers: 10));
        streams.Add(Stream(id: "three", name: "Example offline", provider: "twitchtv", online: false, viewers: 0));
        streams.Add(Stream(id: "channel_handle", name: "Display name", provider: "twitchtv", online: true, viewers: 5));
        model.Filter = "EXAMPLE";
        model.ProviderFilter = "twitchtv";
        model.RebuildRows();
        Assert.Equal("one", Assert.Single(model.Rows).Id);

        // The name filter also matches channel ids.
        model.Filter = "HANDLE";
        model.RebuildRows();
        Assert.Equal("channel_handle", Assert.Single(model.Rows).Id);

        // Top results ignore the followed-channel filters and leave them unchanged.
        var api = AuthorizedProvider(topStreams: true);
        var response = new TopStreamsResponse
        {
            LivestreamModels = [Stream(id: "top", name: "Unrelated", provider: "youtube", online: true, viewers: 100)],
        };

        api.GetTopStreams(Arg.Any<ExternalAPIs.TopStreamQuery>()).Returns(response);
        model.Page = MonitorPage.TopStreams;
        await model.QueryTop(api, "");
        model.RebuildRows();

        Assert.Equal("top", Assert.Single(model.Rows).Id);
        Assert.Equal("HANDLE", model.Filter);
        Assert.Equal("twitchtv", model.ProviderFilter);
    }

    [Theory]
    [InlineData("TARGET", "twitchtv", "TARGET", "twitchtv")]
    [InlineData("missing", "twitchtv", "", "twitchtv")]
    [InlineData("TARGET", "youtube", "TARGET", MonitorWindowState.AllProviders)]
    public void RevealTheLiveTargetByClearingOnlyObstructingFilters(string name, string provider, string expectedName, string expectedProvider)
    {
        var model = Create(savedSettings: new Settings { ShowOnlineOnly = true });
        var stream = Stream(id: "target", name: "Example", provider: "twitchtv", online: false, viewers: 5);
        model.StreamsModel.Livestreams.Add(stream);
        model.Filter = name;
        model.ProviderFilter = provider;
        var target = new StreamNotificationTarget(stream.UniqueStreamKey, stream.ChannelIdentifier.ChannelId, Followed: true);
        Assert.Null(model.FindLiveFollowedStream(target));

        stream.Live = true;
        var live = Assert.IsType<LivestreamModel>(model.FindLiveFollowedStream(target));
        model.RevealFollowedStream(live);
        model.RebuildRows();

        Assert.Same(stream, Assert.Single(model.Rows));
        Assert.Equal(expectedName, model.Filter);
        Assert.Equal(expectedProvider, model.ProviderFilter);
        Assert.True(model.OnlineOnly);
    }

    [Fact]
    public void KeepSelectionWhenTheSavedSortChanges()
    {
        var model = Create();
        var first = Stream(id: "one", name: "Zulu", provider: "twitchtv", online: true, viewers: 100);
        var second = Stream(id: "two", name: "Alpha", provider: "twitchtv", online: true, viewers: 10);
        model.StreamsModel.Livestreams.Add(first);
        model.StreamsModel.Livestreams.Add(second);
        model.RebuildRows();
        model.SelectedIndex = 0;
        model.SettingsHandler.Settings.LivestreamListSortState = new DataGridSortState
        {
            Column = nameof(LivestreamModel.DisplayName),
            SortDirection = ListSortDirection.Ascending,
        };

        model.RebuildRows();
        Assert.Same(second, model.Rows[0]);
        Assert.Same(first, model.SelectedStream);
        Assert.Equal(1, model.SelectedIndex);
    }

    [Fact]
    public void KeepVodSelectionIndependentOfBackgroundChannelRefresh()
    {
        var model = Create();
        var live = Stream(id: "one", name: "Live", provider: "twitchtv", online: true, viewers: 1);
        model.StreamsModel.Livestreams.Add(live);
        model.RebuildRows();
        model.Page = MonitorPage.Vods;
        model.Vods.Add(new VodDetails { Title = "Recorded" });
        model.SelectedIndex = 0;
        model.RebuildRows();
        Assert.Empty(model.Rows);
        Assert.Equal(0, model.SelectedIndex);
        Assert.Null(model.SelectedStream);
    }

    [Theory]
    [InlineData("newer query")]
    [InlineData("page revisit")]
    [InlineData("invalidation")]
    [InlineData("applied filter")]
    public async Task PublishOnlyCurrentGameSuggestions(string supersededBy)
    {
        const string Filter = "Game";
        var model = Create();
        model.Page = MonitorPage.TopStreams;
        var api = AuthorizedProvider(topStreams: true);
        api.HasTopStreamGameFilterSupport.Returns(true);
        api.GetTopStreams(Arg.Any<ExternalAPIs.TopStreamQuery>()).Returns(new TopStreamsResponse());
        var oldRequest = new TaskCompletionSource<List<string>>();
        var currentGames = new List<string> { "Current game" };
        api.GetKnownGameNames(Filter).Returns(oldRequest.Task, Task.FromResult(currentGames));
        var published = new List<string>();
        var oldQuery = model.QueryGameSuggestions(api, Filter, published.AddRange);

        switch (supersededBy)
        {
            case "newer query":
                await model.QueryGameSuggestions(api, Filter, published.AddRange);
                break;
            case "page revisit":
                model.Page = MonitorPage.Vods;
                model.Page = MonitorPage.TopStreams;
                break;
            case "invalidation":
                model.InvalidateGameSuggestions();
                break;
            default:
                await model.QueryTop(api, Filter);
                break;
        }

        oldRequest.SetResult(["Stale game"]);
        await oldQuery;

        if (supersededBy != "newer query")
        {
            Assert.Empty(published);
            await model.QueryGameSuggestions(api, Filter, published.AddRange);
        }

        Assert.Equal("Current game", Assert.Single(published));
    }

    [Theory]
    [InlineData(15, 2, 30, true)]
    [InlineData(30, 1, 30, false)]
    public async Task ApplyPagingAndSupportedGameFilters(int pageSize, int page, int expectedSkip, bool supportsGameFilter)
    {
        var model = Create();
        var api = AuthorizedProvider(topStreams: true);
        api.HasTopStreamGameFilterSupport.Returns(supportsGameFilter);
        api.GetTopStreams(Arg.Any<ExternalAPIs.TopStreamQuery>()).Returns(new TopStreamsResponse());
        model.PageSize = pageSize;
        model.PageNumber = page;
        var expectedGame = supportsGameFilter ? "Example game" : null;

        await model.QueryTop(api, "Example game");

        await api.Received().GetTopStreams(Arg.Is<ExternalAPIs.TopStreamQuery>(query =>
            query.Take == pageSize && query.Skip == expectedSkip && query.GameName == expectedGame));
    }

    private static MonitorWindowState Create(
        IUserDialogs dialogs = null,
        Func<string, string, IDisposable> showProgress = null,
        Settings savedSettings = null,
        Func<string, string, (IDisposable Dialog, Task Completion)> openBrowsingError = null,
        TimeProvider timeProvider = null)
    {
        var settings = Substitute.For<ISettingsHandler>();
        settings.Settings.Returns(savedSettings ?? new Settings());
        var streams = Substitute.For<IMonitorStreamsModel>();
        streams.Livestreams.Returns(new ObservableCollection<LivestreamModel>());
        return new MonitorWindowState(
            dialogs: dialogs ?? Substitute.For<IUserDialogs>(),
            settingsHandler: settings,
            apiClientFactory: Substitute.For<IApiClientFactory>(),
            monitorStreamsModel: streams,
            showProgress: showProgress,
            openBrowsingError: openBrowsingError,
            timeProvider: timeProvider
        );
    }

    private static AggregateException ProviderFailures() => new(new InvalidOperationException("Provider failed."));

    private static IApiClient AuthorizedProvider(bool topStreams = false, bool vods = false)
    {
        var api = Substitute.For<IApiClient>();
        api.HasTopStreamsSupport.Returns(topStreams);
        api.HasVodViewerSupport.Returns(vods);
        api.IsAuthorized.Returns(true);
        return api;
    }

    private static LivestreamModel Stream(
        string id,
        string name,
        string provider,
        bool online,
        long viewers)
    {
        var api = Substitute.For<IApiClient>();
        api.ApiName.Returns(provider);
        var channel = new ChannelIdentifier(api, id);
        return new LivestreamModel(id, channel) { DisplayName = name, Live = online, Viewers = viewers };
    }
}
