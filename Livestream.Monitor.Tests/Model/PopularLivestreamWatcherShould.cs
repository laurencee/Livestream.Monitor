using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Livestream.Monitor.Core;
using Livestream.Monitor.Model;
using Livestream.Monitor.Model.ApiClients;
using Livestream.Monitor.Model.Monitoring;
using Livestream.Monitor.Tests.TestFramework;
using NSubstitute;
using AutoFixture.Xunit2;
using ExternalAPIs;
using Xunit;

namespace Livestream.Monitor.Tests.Model
{
    // these tests suck but are better than nothing, written back when I used to over mock stuff
    public class PopularLivestreamWatcherShould
    {
        [InlineAutoNSubstituteData(1, 2)]
        [InlineAutoNSubstituteData(500, 1)]
        [Theory]
        public async Task NotifyWhenStreamAboveMinimumViewers(
            int minimumViewers,
            int expectedNotificationCount,
            [Frozen] ISettingsHandler settingsHandler,
            [Frozen] INotificationHandler notificationHandler,
            [Frozen] List<IApiClient> apiClients,
            [Frozen] List<LivestreamModel> livestreamModels,
            [Frozen] IApiClientFactory factory,
            PopularLivestreamWatcher sut)
        {
            var apiClient = apiClients.First();
            SetupApiClient(apiClient, livestreamModels);
            factory.GetAll().Returns(apiClients);
            settingsHandler.Settings.MinimumEventViewers = minimumViewers;

            await sut.NotifyPopularStreams();

            notificationHandler.Received(expectedNotificationCount).AddNotification(Arg.Any<LivestreamNotification>());
        }

        [Theory, AutoNSubstituteData]
        public async Task NotNotifyWhenBelowMinimumViewers(
            [Frozen] ISettingsHandler settingsHandler,
            [Frozen] INotificationHandler notificationHandler,
            [Frozen] List<IApiClient> apiClients,
            [Frozen] List<LivestreamModel> livestreamModels,
            [Frozen] IApiClientFactory factory,
            PopularLivestreamWatcher sut)
        {
            var apiClient = apiClients.First();
            SetupApiClient(apiClient, livestreamModels);
            factory.GetAll().Returns(apiClients);
            settingsHandler.Settings.MinimumEventViewers = 2000;

            await sut.NotifyPopularStreams();

            notificationHandler.DidNotReceive().AddNotification(Arg.Any<LivestreamNotification>());
        }

        [InlineAutoNSubstituteData(1, 2)]
        [InlineAutoNSubstituteData(500, 1)]
        [Theory]
        public async Task NotNotifySameStreamWithin1Hr(
            int minimumViewers,
            int expectedNotificationCount,
            [Frozen] ISettingsHandler settingsHandler,
            [Frozen] INotificationHandler notificationHandler,
            [Frozen] List<IApiClient> apiClients,
            [Frozen] List<LivestreamModel> livestreamModels,
            [Frozen] IApiClientFactory factory,
            PopularLivestreamWatcher sut)
        {
            var apiClient = apiClients.First();
            SetupApiClient(apiClient, livestreamModels);
            factory.GetAll().Returns(apiClients);
            settingsHandler.Settings.MinimumEventViewers = minimumViewers;

            await sut.NotifyPopularStreams();
            await sut.NotifyPopularStreams();

            notificationHandler.Received(expectedNotificationCount).AddNotification(Arg.Any<LivestreamNotification>());
        }

        private void SetupApiClient(IApiClient apiClient, List<LivestreamModel> livestreamModels)
        {
            if (livestreamModels.Count < 3)
                throw new InvalidOperationException("Require >= 3 livestream query results for test configuration");

            SetViewerCount(livestreamModels[0], 1000);
            SetViewerCount(livestreamModels[1], 300);
            SetViewerCount(livestreamModels[2], 0);
            apiClient.HasTopStreamsSupport.Returns(true);
            apiClient.IsAuthorized.Returns(true);
            apiClient.GetTopStreams(Arg.Any<TopStreamQuery>()).Returns(Task.FromResult(new TopStreamsResponse()
            {
                LivestreamModels = livestreamModels,
                HasNextPage = false,
            }));
        }

        private void SetViewerCount(LivestreamModel livestreamModel, int viewers)
        {
            livestreamModel.Viewers = viewers;
            if (viewers <= 0) livestreamModel.Offline();
        }
    }
}
