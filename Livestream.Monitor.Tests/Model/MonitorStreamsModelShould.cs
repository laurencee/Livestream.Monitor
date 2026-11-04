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
        provider.QueryChannels(Arg.Any<CancellationToken>()).Returns(new List<LivestreamQueryResult>());
        var sut = CreateModel([new ChannelIdentifier(provider, "channel")], provider);

        sut.BeginInitialQuery();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!sut.IsInitialQueryComplete && DateTime.UtcNow < deadline) await Task.Delay(millisecondsDelay: 10);
        Assert.True(sut.IsInitialQueryComplete);
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

    private static MonitorStreamsModel CreateModel(List<ChannelIdentifier> channels, params IApiClient[] providers)
    {
        var fileHandler = Substitute.For<IMonitoredStreamsFileHandler>();
        fileHandler.LoadFromDisk().Returns(channels);
        var settingsHandler = Substitute.For<ISettingsHandler>();
        settingsHandler.Settings.Returns(new Settings());
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
