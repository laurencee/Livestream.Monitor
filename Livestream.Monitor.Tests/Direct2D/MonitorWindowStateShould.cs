using System;
using System.Collections.Generic;
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

    [Fact]
    public async Task ShowTheInitialContentLoaderUntilInitializationCompletes()
    {
        var model = Create();
        var pending = new TaskCompletionSource();
        model.StreamsModel.Initialize(Arg.Any<IUserDialogs>(), Arg.Any<CancellationToken>()).Returns(pending.Task);
        Assert.True(model.IsContentLoading);

        var initialization = model.Initialize();
        Assert.True(model.IsContentLoading);
        pending.SetResult();
        await initialization;
        Assert.False(model.IsContentLoading);
    }

    [Fact]
    public async Task ShowRowsOnceTheFirstProviderRefreshesWhileStayingBusyUntilInitializationCompletes()
    {
        var progressClosed = false;
        var model = Create(showProgress: (_, _) =>
        {
            var progress = Substitute.For<IDisposable>();
            progress.When(x => x.Dispose()).Do(_ => progressClosed = true);
            return progress;
        });
        var pending = new TaskCompletionSource();
        model.StreamsModel.Initialize(Arg.Any<IUserDialogs>(), Arg.Any<CancellationToken>()).Returns(pending.Task);

        model.StreamsModel.Livestreams.Add(Stream("offline", "Channel", "youtube", online: false, viewers: 0));
        var initialization = model.Initialize();
        Assert.False(progressClosed);
        model.StreamsModel.ProviderRefreshComplete += Raise.Event<EventHandler>();
        Assert.True(progressClosed);
        Assert.False(model.IsContentLoading);
        Assert.True(model.Busy);

        pending.SetResult();
        await initialization;
        Assert.False(model.Busy);
        Assert.False(model.FollowedRowsSettled);

        model.StreamsModel.CanRefreshLivestreams.Returns(true);
        Assert.True(model.FollowedRowsSettled);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task ShowTheContentLoaderDuringTopStreamRequestsIncludingPaging(int page)
    {
        var model = Create();
        await model.Initialize();
        var api = AuthorizedProvider(topStreams: true);
        var pending = new TaskCompletionSource<TopStreamsResponse>();
        api.GetTopStreams(Arg.Any<ExternalAPIs.TopStreamQuery>()).Returns(pending.Task);
        model.PageNumber = page;

        var query = model.QueryTop(api, "");
        Assert.True(model.IsContentLoading);
        pending.SetResult(new TopStreamsResponse());
        await query;
        Assert.False(model.IsContentLoading);
    }

    [Fact]
    public async Task ClearTheVodLoaderAfterARequestFails()
    {
        var model = Create();
        await model.Initialize();
        var api = AuthorizedProvider(vods: true);
        var pending = new TaskCompletionSource<IReadOnlyCollection<VodDetails>>();
        api.GetVods(Arg.Any<VodQuery>()).Returns(pending.Task);

        var query = model.QueryVods(api, "Example", "archive");
        Assert.True(model.IsContentLoading);
        pending.SetException(new InvalidOperationException("Controlled provider failure"));
        await query;
        Assert.False(model.IsContentLoading);
        Assert.False(model.Busy);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task ShowTheContentLoaderUntilVodResultsArriveIncludingPaging(int page)
    {
        var model = Create();
        await model.Initialize();
        var api = AuthorizedProvider(vods: true);
        var pending = new TaskCompletionSource<IReadOnlyCollection<VodDetails>>();
        api.GetVods(Arg.Any<VodQuery>()).Returns(pending.Task);
        model.PageNumber = page;

        var query = model.QueryVods(api, "Example", "archive");
        Assert.True(model.IsContentLoading);
        pending.SetResult([new VodDetails { Title = "Recorded" }]);
        await query;
        Assert.False(model.IsContentLoading);
        Assert.Single(model.Vods);
    }

    [Fact]
    public async Task ClearOldTopStreamResultsWhenTheNextPageFails()
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
            .Returns(Task.FromResult(firstPage), Task.FromException<TopStreamsResponse>(failure));
        await model.QueryTop(api, "");

        model.PageNumber = 1;
        await model.QueryTop(api, "");
        model.RebuildRows();

        Assert.Empty(model.Rows);
        Assert.False(model.HasNextPage);
    }

    [Fact]
    public async Task ClearOldVodResultsWhenTheNewChannelFails()
    {
        var model = Create();
        model.Page = MonitorPage.Vods;
        var api = AuthorizedProvider(vods: true);
        IReadOnlyCollection<VodDetails> firstChannel = [new() { Title = "Old VOD" }];
        var failure = new InvalidOperationException("Controlled provider failure");
        api.GetVods(Arg.Any<VodQuery>())
            .Returns(Task.FromResult(firstChannel), Task.FromException<IReadOnlyCollection<VodDetails>>(failure));
        await model.QueryVods(api, "old", "archive");

        await model.QueryVods(api, "new", "archive");

        Assert.Empty(model.Vods);
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
    public async Task KeepTheTableVisibleDuringOrdinaryFollowedStreamRefreshes()
    {
        var model = Create();
        await model.Initialize();
        var pending = new TaskCompletionSource();
        model.StreamsModel.RefreshLivestreams().Returns(pending.Task);

        var refresh = model.Refresh();
        Assert.False(model.IsContentLoading);
        pending.SetResult();
        await refresh;
        Assert.False(model.IsContentLoading);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReportWhetherAChannelWasAdded(bool fails)
    {
        // The window clears the add input only after a successful add, as WPF does.
        var model = Create();
        var failure = new InvalidOperationException("Controlled provider failure");
        var result = fails ? Task.FromException(failure) : Task.CompletedTask;
        model.StreamsModel.AddLivestream(Arg.Any<ChannelIdentifier>(), Arg.Any<IUserDialogs>()).Returns(result);

        var added = await model.Add("example", Substitute.For<IApiClient>());

        Assert.Equal(!fails, added);
        Assert.False(model.Busy);
    }

    [Fact]
    public async Task ShowOtherFailuresEvenWhenRefreshErrorDialogsAreDisabled()
    {
        var dialogs = Substitute.For<IUserDialogs>();
        var model = Create(dialogs);
        model.SettingsHandler.Settings.DisableRefreshErrorDialogs = true;
        var failure = Task.FromException(new InvalidOperationException("Controlled provider failure"));
        model.StreamsModel.AddLivestream(Arg.Any<ChannelIdentifier>(), Arg.Any<IUserDialogs>()).Returns(failure);

        await model.Add("example", Substitute.For<IApiClient>());

        await dialogs.Received(1).ShowMessage("Error adding stream", Arg.Any<string>());
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

        var model = Create(dialogs, ShowProgress);
        var failure = Task.FromException(new InvalidOperationException("Controlled provider failure"));
        model.StreamsModel.AddLivestream(Arg.Any<ChannelIdentifier>(), Arg.Any<IUserDialogs>()).Returns(failure);

        await model.Add("example", Substitute.For<IApiClient>());

        Assert.Equal(["progress shown", "progress closed", "error shown"], events);

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
        model.StreamsModel.RefreshLivestreams().Returns(_ => Task.FromException(ProviderFailures()));
        int[] expectedReports = [0, 0, 1, 2];

        foreach (var expected in expectedReports)
        {
            await model.Refresh(automatic: true);
            await dialogs.Received(expected).Confirm(RefreshErrorTitle, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
        }
    }

    [Fact]
    public async Task HideOnlyAutomaticRefreshFailuresWhenRefreshErrorDialogsAreDisabled()
    {
        var dialogs = Substitute.For<IUserDialogs>();
        var model = Create(dialogs);
        model.SettingsHandler.Settings.DisableRefreshErrorDialogs = true;
        model.StreamsModel.RefreshLivestreams().Returns(_ => Task.FromException(ProviderFailures()));

        for (var i = 0; i < 3; i++) await model.Refresh(automatic: true);
        await dialogs.DidNotReceive().Confirm(RefreshErrorTitle, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());

        await model.Refresh();
        await dialogs.Received(1).Confirm(RefreshErrorTitle, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task ReportNoAutomaticRefreshFailuresOffTheFollowedPage()
    {
        var dialogs = Substitute.For<IUserDialogs>();
        var model = Create(dialogs);
        model.StreamsModel.RefreshLivestreams().Returns(_ => Task.FromException(new InvalidOperationException("Failed.")));

        foreach (var page in new[] { MonitorPage.TopStreams, MonitorPage.Vods })
        {
            model.Page = page;
            for (var i = 0; i < 3; i++) await model.Refresh(automatic: true);
        }

        await dialogs.DidNotReceiveWithAnyArgs().ShowMessage(null, null);
    }

    [Fact]
    public async Task RestartTheAutomaticRefreshFailureCountWhenTheFollowedPageIsShownAgain()
    {
        var dialogs = Substitute.For<IUserDialogs>();
        var model = Create(dialogs);
        model.StreamsModel.RefreshLivestreams().Returns(_ => Task.FromException(ProviderFailures()));

        for (var i = 0; i < 2; i++) await model.Refresh(automatic: true);

        model.Page = MonitorPage.TopStreams;
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

    [Theory]
    [InlineData(0, "0")]
    [InlineData(93784, "26h 03m 04s")]
    public void MatchTheWpfUptimeFormat(int seconds, string expected)
    {
        Assert.Equal(expected, TimeSpan.FromSeconds(seconds).ToTotalHoursText());
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
    }

    [Fact]
    public void CombineNameProviderAndOnlineFilters()
    {
        var settings = new Settings { ShowOnlineOnly = true };
        var model = Create(savedSettings: settings);
        model.StreamsModel.Livestreams.AddRange([
            Stream(id: "one", name: "Example", provider: "twitchtv", online: true, viewers: 5),
            Stream(id: "two", name: "Example other", provider: "youtube", online: true, viewers: 10),
            Stream(id: "three", name: "Example offline", provider: "twitchtv", online: false, viewers: 0),
        ]);
        model.Filter = "EXAMPLE";
        model.ProviderFilter = "twitchtv";
        model.RebuildRows();
        Assert.Equal("one", Assert.Single(model.Rows).Id);
    }

    [Fact]
    public void MatchTheNameFilterAgainstChannelIdsAsWpfDoes()
    {
        var model = Create();
        model.StreamsModel.Livestreams.AddRange([
            Stream(id: "channel_handle", name: "Display name", provider: "twitchtv", online: true, viewers: 5),
            Stream(id: "other", name: "Other", provider: "twitchtv", online: true, viewers: 5),
        ]);
        model.Filter = "HANDLE";
        model.RebuildRows();
        Assert.Equal("channel_handle", Assert.Single(model.Rows).Id);
    }

    [Theory]
    [InlineData("TARGET", "twitchtv", "TARGET", "twitchtv")]
    [InlineData("missing", "twitchtv", "", "twitchtv")]
    [InlineData("TARGET", "youtube", "TARGET", FilterModel.AllApiClientsFilterName)]
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
        model.StreamsModel.Livestreams.AddRange([first, second]);
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

    [Fact]
    public async Task ClearPreviousTopResultsWhenAProviderDoesNotSupportThem()
    {
        var model = Create();
        var supported = AuthorizedProvider(topStreams: true);
        var response = new TopStreamsResponse
        {
            LivestreamModels = [Stream(id: "one", name: "Live", provider: "twitchtv", online: true, viewers: 1)],
            HasNextPage = true,
        };

        supported.GetTopStreams(Arg.Any<ExternalAPIs.TopStreamQuery>()).Returns(response);
        model.Page = MonitorPage.TopStreams;
        await model.QueryTop(supported, "");
        model.RebuildRows();
        Assert.Single(model.Rows);
        await model.QueryTop(Substitute.For<IApiClient>(), "");
        model.RebuildRows();
        Assert.Empty(model.Rows);
        Assert.False(model.HasNextPage);
    }

    [Fact]
    public async Task KeepTopResultsIndependentOfFollowedChannelFilters()
    {
        var model = Create();
        model.Filter = "unrelated followed-channel name";
        model.ProviderFilter = "youtube";
        var api = AuthorizedProvider(topStreams: true);
        var response = new TopStreamsResponse
        {
            LivestreamModels = [Stream(id: "one", name: "Example", provider: "twitchtv", online: true, viewers: 100)],
        };

        api.GetTopStreams(Arg.Any<ExternalAPIs.TopStreamQuery>()).Returns(response);
        model.Page = MonitorPage.TopStreams;
        await model.QueryTop(api, "");
        model.RebuildRows();

        Assert.Equal("one", Assert.Single(model.Rows).Id);
        Assert.Equal("unrelated followed-channel name", model.Filter);
        Assert.Equal("youtube", model.ProviderFilter);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PublishOnlyCurrentGameSuggestions(bool revisitPage)
    {
        const string Filter = "Game";
        var model = Create();
        model.Page = MonitorPage.TopStreams;
        var api = AuthorizedProvider(topStreams: true);
        var oldRequest = new TaskCompletionSource<List<KnownGame>>();
        var currentGames = new List<KnownGame> { new() { GameName = "Current game" } };
        api.GetKnownGameNames(Filter).Returns(oldRequest.Task, Task.FromResult(currentGames));
        var published = new List<KnownGame>();
        var oldQuery = model.QueryGameSuggestions(api, Filter, published.AddRange);

        if (revisitPage)
        {
            model.Page = MonitorPage.Vods;
            model.Page = MonitorPage.TopStreams;
        }
        else
        {
            await model.QueryGameSuggestions(api, Filter, published.AddRange);
        }

        oldRequest.SetResult([new KnownGame { GameName = "Stale game" }]);
        await oldQuery;

        if (revisitPage)
        {
            Assert.Empty(published);
            await model.QueryGameSuggestions(api, Filter, published.AddRange);
        }

        Assert.Equal("Current game", Assert.Single(published).GameName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DropPendingGameSuggestionsOnInvalidationOrFilterApply(bool applyFilter)
    {
        const string Filter = "Game";
        var model = Create();
        model.Page = MonitorPage.TopStreams;
        var api = AuthorizedProvider(topStreams: true);
        if (applyFilter)
        {
            api.HasTopStreamGameFilterSupport.Returns(true);
            api.GetTopStreams(Arg.Any<ExternalAPIs.TopStreamQuery>()).Returns(new TopStreamsResponse());
        }

        var pending = new TaskCompletionSource<List<KnownGame>>();
        api.GetKnownGameNames(Filter).Returns(pending.Task);
        var published = new List<KnownGame>();
        var suggestions = model.QueryGameSuggestions(api, Filter, published.AddRange);

        if (applyFilter) await model.QueryTop(api, Filter);
        else model.InvalidateGameSuggestions();

        pending.SetResult([new KnownGame { GameName = "Stale game" }]);
        await suggestions;

        Assert.Empty(published);
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

    [Fact]
    public async Task KeepTheStatusOnOneLineWhileTheDialogListsEveryFailure()
    {
        var dialogs = Substitute.For<IUserDialogs>();
        var model = Create(dialogs);
        var failure = new AggregateException(new InvalidOperationException("First failed."), new InvalidOperationException("Second failed."));

        await model.Run("Error refreshing livestreams", () => Task.FromException(failure));

        Assert.DoesNotContain('\n', model.Status);
        Assert.DoesNotContain('\r', model.Status);
        Assert.Contains("First failed.", model.Status);
        Assert.Contains("Second failed.", model.Status);
        await dialogs.Received().ShowMessage(Arg.Any<string>(), $"First failed.{Environment.NewLine}Second failed.");
    }

    private static MonitorWindowState Create(
        IUserDialogs dialogs = null,
        Func<string, string, IDisposable> showProgress = null,
        Settings savedSettings = null,
        Func<string, string, (IDisposable Dialog, Task Completion)> openBrowsingError = null)
    {
        var settings = Substitute.For<ISettingsHandler>();
        settings.Settings.Returns(savedSettings ?? new Settings());
        var streams = Substitute.For<IMonitorStreamsModel>();
        streams.Livestreams.Returns(new ObservableList<LivestreamModel>());
        return new MonitorWindowState(
            dialogs: dialogs ?? Substitute.For<IUserDialogs>(),
            settingsHandler: settings,
            apiClientFactory: Substitute.For<IApiClientFactory>(),
            monitorStreamsModel: streams,
            showProgress: showProgress,
            openBrowsingError: openBrowsingError
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
