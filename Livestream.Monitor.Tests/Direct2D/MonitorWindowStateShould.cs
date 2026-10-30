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

        var initialization = model.Initialize();
        Assert.False(progressClosed);
        model.StreamsModel.ProviderRefreshComplete += Raise.Event<EventHandler>();
        Assert.True(progressClosed);
        Assert.False(model.IsContentLoading);
        Assert.True(model.Busy);

        pending.SetResult();
        await initialization;
        Assert.False(model.Busy);
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

        Assert.Equal(["Adding stream shown", "progress closed", "error shown"], events);

        IDisposable ShowProgress(string title, string message)
        {
            events.Add($"{title} shown");
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
        var model = Create();
        model.StreamsModel.Livestreams.AddRange([
            Stream(id: "one", name: "Example", provider: "twitchtv", online: true, viewers: 5),
            Stream(id: "two", name: "Example other", provider: "youtube", online: true, viewers: 10),
            Stream(id: "three", name: "Example offline", provider: "twitchtv", online: false, viewers: 0),
        ]);
        model.Filter = "EXAMPLE";
        model.ProviderFilter = "twitchtv";
        model.OnlineOnly = true;
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
    [InlineData(15, 2, 30)]
    [InlineData(30, 1, 30)]
    public async Task ApplyTheSelectedPageSizeToProviderRequests(int pageSize, int page, int expectedSkip)
    {
        var model = Create();
        var api = AuthorizedProvider(topStreams: true);
        api.GetTopStreams(Arg.Any<ExternalAPIs.TopStreamQuery>()).Returns(new TopStreamsResponse());
        model.PageSize = pageSize;
        model.PageNumber = page;

        await model.QueryTop(api, "Example game");

        await api.Received().GetTopStreams(Arg.Is<ExternalAPIs.TopStreamQuery>(query =>
            query.Take == pageSize && query.Skip == expectedSkip && query.GameName == "Example game"));
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

    private static MonitorWindowState Create(IUserDialogs dialogs = null, Func<string, string, IDisposable> showProgress = null)
    {
        var settings = Substitute.For<ISettingsHandler>();
        settings.Settings.Returns(new Settings());
        var streams = Substitute.For<IMonitorStreamsModel>();
        streams.Livestreams.Returns(new ObservableList<LivestreamModel>());
        return new MonitorWindowState(
            dialogs: dialogs ?? Substitute.For<IUserDialogs>(),
            settingsHandler: settings,
            apiClientFactory: Substitute.For<IApiClientFactory>(),
            monitorStreamsModel: streams,
            showProgress: showProgress
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
