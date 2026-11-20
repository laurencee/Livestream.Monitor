using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using ExternalAPIs;
using Livestream.Monitor.Core;
using Livestream.Monitor.Core.Settings;
using Livestream.Monitor.Model;
using Livestream.Monitor.Model.ApiClients;
using Livestream.Monitor.Model.Monitoring;
using NSubstitute;
using Xunit;

namespace Livestream.Monitor.Tests.Model;

public class MonitorStreamsModelShould
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReportAFailedProviderRequestOnceAndKeepOtherProvidersResults(bool queryBeforeInitializing)
    {
        var failure = new HttpRequestWithStatusException(HttpStatusCode.ServiceUnavailable, "Unavailable.");
        var failingProvider = CreateFailingProvider("failing", failure);

        var workingProvider = CreateProvider("working");
        var workingChannel = new ChannelIdentifier(workingProvider, "live");
        var liveStream = new LivestreamModel("live", workingChannel) { DisplayName = "live", Live = true };
        List<LivestreamQueryResult> workingResults = [new(workingChannel) { LivestreamModel = liveStream }];
        workingProvider.QueryChannels(Arg.Any<CancellationToken>()).Returns(workingResults);

        List<ChannelIdentifier> channels =
        [
            new(failingProvider, "first"),
            new(failingProvider, "second"),
            workingChannel,
        ];
        var sut = CreateModel(channels, failingProvider, workingProvider);

        if (queryBeforeInitializing) sut.BeginInitialQuery();
        var refreshFailure = await Assert.ThrowsAsync<AggregateException>(() => sut.Initialize(Substitute.For<IUserDialogs>()));

        var queryFailure = Assert.IsType<FailedQueryException>(Assert.Single(refreshFailure.InnerExceptions));
        Assert.Same(failure, queryFailure.InnerException);
        Assert.True(sut.Livestreams.Single(x => Equals(x.ChannelIdentifier, workingChannel)).Live);
        var failedChannels = sut.Livestreams.Where(x => x.ApiClient == failingProvider).ToList();
        Assert.Equal(2, failedChannels.Count);
        Assert.All(failedChannels, x => Assert.False(x.Live));
    }

    [Fact]
    public async Task DescribeEveryFailedProviderInTheErrorMessage()
    {
        var firstProvider = CreateFailingProvider("first-provider", new TimeoutException("First timed out."));
        var secondProvider = CreateFailingProvider("second-provider", new TimeoutException("Second timed out."));
        List<ChannelIdentifier> channels = [new(firstProvider, "first"), new(secondProvider, "second")];
        var sut = CreateModel(channels, firstProvider, secondProvider);

        var refreshFailure = await Assert.ThrowsAsync<AggregateException>(() => sut.Initialize(Substitute.For<IUserDialogs>()));

        var message = refreshFailure.ExtractErrorMessage();
        Assert.Contains("first-provider", message);
        Assert.Contains("First timed out.", message);
        Assert.Contains("second-provider", message);
        Assert.Contains("Second timed out.", message);
    }

    [Fact]
    public async Task ApplyAFinishedProvidersResultsBeforeASlowerProviderFinishes()
    {
        var slowProvider = CreateProvider("slow");
        var slowQuery = new TaskCompletionSource<List<LivestreamQueryResult>>();
        slowProvider.QueryChannels(Arg.Any<CancellationToken>()).Returns(slowQuery.Task);

        var fastProvider = CreateProvider("fast");
        var fastChannel = new ChannelIdentifier(fastProvider, "live");
        var liveStream = new LivestreamModel("live", fastChannel) { DisplayName = "live", Live = true };
        List<LivestreamQueryResult> fastResults = [new(fastChannel) { LivestreamModel = liveStream }];
        fastProvider.QueryChannels(Arg.Any<CancellationToken>()).Returns(fastResults);

        // An empty provider mustn't count as the first results.
        var idleProvider = CreateProvider("idle");
        idleProvider.QueryChannels(Arg.Any<CancellationToken>()).Returns(new List<LivestreamQueryResult>());

        var slowChannel = new ChannelIdentifier(slowProvider, "slow");
        var sut = CreateModel([slowChannel, fastChannel], idleProvider, slowProvider, fastProvider);
        // Capture the state in the event handler; more results may arrive before the test resumes.
        var liveAtFirstProvider = new TaskCompletionSource<bool>();
        sut.ProviderRefreshComplete += (_, _) =>
            liveAtFirstProvider.TrySetResult(sut.Livestreams.Single(x => Equals(x.ChannelIdentifier, fastChannel)).Live);

        var initialization = sut.Initialize(Substitute.For<IUserDialogs>());

        Assert.True(await liveAtFirstProvider.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(sut.Initialised);
        var slowStream = new LivestreamModel("slow", slowChannel) { DisplayName = "slow" };
        slowQuery.SetResult([new LivestreamQueryResult(slowChannel) { LivestreamModel = slowStream }]);
        await initialization;
        Assert.True(sut.Initialised);
    }

    [Fact]
    public async Task InitializeFromAQuickQueryAndApplyTheCompleteResultsWithinTheFirstRefresh()
    {
        var provider = CreateProvider("quick");
        provider.HasQuickQuerySupport.Returns(true);
        var channel = new ChannelIdentifier(provider, "channel");
        var offlineStream = new LivestreamModel("channel", channel) { DisplayName = "channel" };
        List<LivestreamQueryResult> quickResults = [new(channel) { LivestreamModel = offlineStream }];
        provider.QueryChannelsQuickly(Arg.Any<CancellationToken>()).Returns(quickResults);
        var completeQuery = new TaskCompletionSource<List<LivestreamQueryResult>>();
        provider.QueryChannels(Arg.Any<CancellationToken>()).Returns(completeQuery.Task);
        var sut = CreateModel([channel], provider);
        var firstRefreshComplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sut.LivestreamsRefreshComplete += (_, _) => firstRefreshComplete.TrySetResult();

        sut.BeginInitialQuery();
        await sut.Initialize(Substitute.For<IUserDialogs>());

        Assert.True(sut.Initialised);
        Assert.False(sut.Livestreams.Single().Live);
        Assert.False(sut.CanRefreshLivestreams);
        Assert.False(firstRefreshComplete.Task.IsCompleted);

        var liveStream = new LivestreamModel("channel", channel) { DisplayName = "channel", Live = true };
        completeQuery.SetResult([new LivestreamQueryResult(channel) { LivestreamModel = liveStream }]);
        await firstRefreshComplete.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(sut.Livestreams.Single().Live);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NotifyOptedInStreamsOrAllUnmutedStreamsWhenGloballyEnabled(bool globallyEnabled)
    {
        var provider = CreateProvider(TwitchApiClient.API_NAME);
        var enabled = new ChannelIdentifier(provider, "enabled");
        var defaultChannel = new ChannelIdentifier(provider, "default");
        var muted = new ChannelIdentifier(provider, "muted");
        var unchanged = new ChannelIdentifier(provider, "unchanged");
        List<ChannelIdentifier> channels = [enabled, defaultChannel, muted, unchanged];
        var initialResults = channels.Select(channel => CategoryResult(channel, "First category")).ToList();
        var completeQuery = new TaskCompletionSource<List<LivestreamQueryResult>>();
        provider.HasQuickQuerySupport.Returns(true);
        provider.QueryChannelsQuickly(Arg.Any<CancellationToken>()).Returns(initialResults);
        provider.QueryChannels(Arg.Any<CancellationToken>()).Returns(completeQuery.Task);
        var settings = new Settings();
        settings.ExcludeFromNotifying.Add(new UniqueStreamKey(provider.ApiName, muted.ChannelId));
        var fileHandler = Substitute.For<IMonitoredStreamsFileHandler>();
        var sut = CreateModel(channels, fileHandler, providers: [provider], settings: settings);
        var refreshComplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sut.LivestreamsRefreshComplete += (_, _) => refreshComplete.SetResult();
        var notifications = new List<LivestreamNotification>();
        sut.CategoryChangeNotification += notifications.Add;

        sut.BeginInitialQuery();
        await sut.Initialize(Substitute.For<IUserDialogs>());
        Assert.Empty(notifications);
        var enabledStream = sut.Livestreams.Single(stream => stream.Id == enabled.ChannelId);
        settings.ToggleCategoryChangeNotifications(enabledStream.UniqueStreamKey);
        settings.ToggleCategoryChangeNotifications(new UniqueStreamKey(provider.ApiName, muted.ChannelId));
        settings.ToggleCategoryChangeNotifications(new UniqueStreamKey(provider.ApiName, unchanged.ChannelId));
        settings.EnableCategoryChangeNotifications = globallyEnabled;

        const string NewCategory = "New category";
        const string NewDescription = "A new stream description";
        var newCategoryResult = CategoryResult(enabled, NewCategory);
        newCategoryResult.LivestreamModel.Description = NewDescription;
        newCategoryResult.LivestreamModel.ThumbnailUrls = new ThumbnailUrls { Small = "https://example.com/thumbnail.jpg" };
        var defaultCategoryResult = CategoryResult(defaultChannel, NewCategory);
        var mutedCategoryResult = CategoryResult(muted, NewCategory);
        completeQuery.SetResult([newCategoryResult, defaultCategoryResult, mutedCategoryResult, initialResults[^1]]);
        await refreshComplete.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(globallyEnabled ? 2 : 1, notifications.Count);
        Assert.Equal(globallyEnabled, notifications.Any(item => item.LivestreamModel.Id == defaultChannel.ChannelId));
        var notification = Assert.Single(notifications, item => ReferenceEquals(item.LivestreamModel, enabledStream));
        var onlineNotification = LivestreamNotification.CreateForFollowedStream(enabledStream);
        Assert.Equal($"{enabledStream.DisplayName} Online", onlineNotification.Title);
        Assert.Equal($"{enabledStream.DisplayName} - {NewCategory}", notification.Title);
        Assert.Equal(NewDescription, notification.Message);
        Assert.Equal(onlineNotification.Message, notification.Message);
        Assert.Equal(newCategoryResult.LivestreamModel.ThumbnailUrls.Small, notification.ImageUrl);
        Assert.Equal(onlineNotification.ImageUrl, notification.ImageUrl);

        static LivestreamQueryResult CategoryResult(ChannelIdentifier channel, string category)
        {
            var stream = new LivestreamModel(channel.ChannelId, channel)
            {
                DisplayName = channel.ChannelId,
                Live = true,
                Game = category,
            };
            return new LivestreamQueryResult(channel) { LivestreamModel = stream };
        }
    }

    [Fact]
    public void TrackKnownCategoriesWithinEachLiveSession()
    {
        var provider = CreateProvider(KickApiClient.API_NAME);
        var channel = new ChannelIdentifier(provider, "channel");
        var start = DateTimeOffset.UtcNow;
        var stream = Snapshot(category: null, id: null);
        var first = Snapshot("First category", "1");
        var sameId = Snapshot("Renamed category", "1");
        var second = Snapshot("Renamed category", "2");
        var newSession = Snapshot("First category", "1");
        newSession.StartTime = start.AddHours(1);
        var offline = Snapshot("Offline category", "3");
        offline.Live = false;

        Assert.Null(stream.ApplyUpdate(first));
        Assert.Null(stream.ApplyUpdate(sameId));
        Assert.Null(stream.ApplyUpdate(Snapshot(category: null, id: null)));
        Assert.Equal(second.Game, stream.ApplyUpdate(second));
        Assert.Null(stream.ApplyUpdate(second));
        Assert.Null(stream.ApplyUpdate(newSession));
        Assert.Null(stream.ApplyUpdate(offline));
        Assert.Null(stream.ApplyUpdate(second));

        LivestreamModel Snapshot(string category, string id) => new(channel.ChannelId, channel)
        {
            DisplayName = channel.ChannelId,
            Live = true,
            StartTime = start,
            Game = category,
            GameId = id,
        };
    }

    [Fact]
    public async Task ReportAQuickQueryFailureFromInitializeWhileTheCompleteQueryRuns()
    {
        var provider = CreateProvider("quick");
        provider.HasQuickQuerySupport.Returns(true);
        var failure = new TimeoutException("Quick timed out.");
        provider.QueryChannelsQuickly(Arg.Any<CancellationToken>()).Returns(Task.FromException<List<LivestreamQueryResult>>(failure));
        provider.QueryChannels(Arg.Any<CancellationToken>()).Returns(new TaskCompletionSource<List<LivestreamQueryResult>>().Task);
        var sut = CreateModel([new ChannelIdentifier(provider, "channel")], provider);

        sut.BeginInitialQuery();
        var refreshFailure = await Assert.ThrowsAsync<AggregateException>(() => sut.Initialize(Substitute.For<IUserDialogs>()));

        var queryFailure = Assert.IsType<FailedQueryException>(Assert.Single(refreshFailure.InnerExceptions));
        Assert.Same(failure, queryFailure.InnerException);
    }

    [Fact]
    public async Task AuthorizeAndQueryAPromptFreeProviderBeforeInitializing()
    {
        var provider = CreateProvider("prompt-free");
        var authorized = false;
        provider.IsAuthorized.Returns(_ => authorized);
        provider.CanAuthorizeWithoutUserInput.Returns(true);
        provider.When(x => x.Authorize(Arg.Any<IUserDialogs>())).Do(_ => authorized = true);
        var queried = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.QueryChannels(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            queried.SetResult();
            return Task.FromResult(new List<LivestreamQueryResult>());
        });

        var sut = CreateModel([new ChannelIdentifier(provider, "channel")], provider);

        sut.BeginInitialQuery();
        await queried.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await sut.Initialize(Substitute.For<IUserDialogs>());

        await provider.Received(1).Authorize(null);
        await provider.Received(1).QueryChannels(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LeaveAuthorizationThatNeedsPromptsToInitialization()
    {
        var provider = CreateProvider("prompting");
        provider.IsAuthorized.Returns(false);
        provider.QueryChannels(Arg.Any<CancellationToken>()).Returns(new List<LivestreamQueryResult>());
        var sut = CreateModel([new ChannelIdentifier(provider, "channel")], provider);
        var dialogs = Substitute.For<IUserDialogs>();

        sut.BeginInitialQuery();
        await sut.Initialize(dialogs);

        await provider.Received(1).Authorize(dialogs);
        await provider.DidNotReceive().Authorize(null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveALoginLearnedDuringPollingEvenWhenTheDisplayNameIsUnchanged(bool queryBeforeInitializing)
    {
        var provider = CreateProvider(TwitchApiClient.API_NAME);
        var channel = new ChannelIdentifier(provider, "42") { DisplayName = "한동숙" };
        var stream = new LivestreamModel("42", channel) { DisplayName = channel.DisplayName };
        List<LivestreamQueryResult> results = [new(channel) { LivestreamModel = stream }];
        provider.QueryChannels(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            channel.TwitchLogin = "known_login";
            return results;
        });
        var fileHandler = Substitute.For<IMonitoredStreamsFileHandler>();
        var sut = CreateModel([channel], fileHandler, [provider]);
        var refreshComplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sut.LivestreamsRefreshComplete += (_, _) => refreshComplete.TrySetResult();

        if (queryBeforeInitializing) sut.BeginInitialQuery();
        await sut.Initialize(Substitute.For<IUserDialogs>());
        await refreshComplete.Task;

        fileHandler.Received(1).SaveToDisk(Arg.Is<IEnumerable<ChannelIdentifier>>(channels => channels.Single().TwitchLogin == "known_login"));
    }

    [Fact]
    public async Task SaveALoginLearnedBetweenRefreshesAndStopWatchingARemovedChannel()
    {
        var provider = CreateProvider(TwitchApiClient.API_NAME);
        var channel = new ChannelIdentifier(provider, "42") { DisplayName = "한동숙" };
        var stream = new LivestreamModel("42", channel) { DisplayName = channel.DisplayName };
        List<LivestreamQueryResult> results = [new(channel) { LivestreamModel = stream }];
        provider.QueryChannels(Arg.Any<CancellationToken>()).Returns(results);
        var fileHandler = Substitute.For<IMonitoredStreamsFileHandler>();
        var sut = CreateModel([channel], fileHandler, [provider]);
        var refreshComplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sut.LivestreamsRefreshComplete += (_, _) => refreshComplete.TrySetResult();
        await sut.Initialize(Substitute.For<IUserDialogs>());
        await refreshComplete.Task;
        fileHandler.ClearReceivedCalls();

        channel.TwitchLogin = "known_login";

        fileHandler.Received(1).SaveToDisk(Arg.Any<IEnumerable<ChannelIdentifier>>());

        await sut.RemoveLivestream(channel);
        fileHandler.ClearReceivedCalls();
        channel.TwitchLogin = "renamed_login";

        fileHandler.DidNotReceive().SaveToDisk(Arg.Any<IEnumerable<ChannelIdentifier>>());
    }

    private static MonitorStreamsModel CreateModel(List<ChannelIdentifier> channels, params IApiClient[] providers) =>
        CreateModel(channels, Substitute.For<IMonitoredStreamsFileHandler>(), providers);

    private static MonitorStreamsModel CreateModel(
        List<ChannelIdentifier> channels,
        IMonitoredStreamsFileHandler fileHandler,
        IApiClient[] providers,
        Settings settings = null)
    {
        fileHandler.LoadFromDisk().Returns(channels);
        var settingsHandler = Substitute.For<ISettingsHandler>();
        settingsHandler.Settings.Returns(settings ?? new Settings());
        var factory = Substitute.For<IApiClientFactory>();
        factory.GetAll().Returns(providers);
        return new MonitorStreamsModel(fileHandler, settingsHandler, factory);
    }

    private static IApiClient CreateFailingProvider(string name, Exception failure)
    {
        var provider = CreateProvider(name);
        provider.QueryChannels(Arg.Any<CancellationToken>()).Returns(Task.FromException<List<LivestreamQueryResult>>(failure));
        return provider;
    }

    private static IApiClient CreateProvider(string name)
    {
        var provider = Substitute.For<IApiClient>();
        provider.ApiName.Returns(name);
        provider.IsAuthorized.Returns(true);
        provider.Initialize(Arg.Any<CancellationToken>()).Returns(new InitializeApiClientResult());
        return provider;
    }
}
