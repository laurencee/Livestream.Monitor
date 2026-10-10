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
