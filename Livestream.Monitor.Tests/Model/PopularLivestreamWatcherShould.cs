using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ExternalAPIs.TwitchTv.Query;
using Livestream.Monitor.Core;
using Livestream.Monitor.Model;
using Livestream.Monitor.Model.ApiClients;
using Livestream.Monitor.Model.Monitoring;
using Livestream.Monitor.Tests.TestFramework;
using NSubstitute;
using Ploeh.AutoFixture.Xunit2;
using Xunit;

namespace Livestream.Monitor.Tests.Model
{
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
            [Frozen] List<LivestreamQueryResult> livestreamQueryResults,
            PopularLivestreamWatcher sut)
        {
            var apiClient = apiClients.First();
            SetupApiClient(apiClient, livestreamQueryResults);
            settingsHandler.Settings.MinimumEventViewers = minimumViewers;
            
            await sut.NotifyPopularStreams();

            notificationHandler.Received(expectedNotificationCount).AddNotification(Arg.Any<LivestreamNotification>());
        }

        [Theory, AutoNSubstituteData]
        public async Task NotNotifyWhenBelowMinimumViewers(
            [Frozen] ISettingsHandler settingsHandler,
            [Frozen] INotificationHandler notificationHandler,
            [Frozen] IEnumerable<IApiClient> apiClients,
            [Frozen] List<LivestreamQueryResult> livestreamQueryResults,
            PopularLivestreamWatcher sut)
        {
            var apiClient = apiClients.First();
            SetupApiClient(apiClient, livestreamQueryResults);
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
            [Frozen] IEnumerable<IApiClient> apiClients,
            [Frozen] List<LivestreamQueryResult> livestreamQueryResults,
            PopularLivestreamWatcher sut)
        {
            var apiClient = apiClients.First();
            SetupApiClient(apiClient, livestreamQueryResults);
            settingsHandler.Settings.MinimumEventViewers = minimumViewers;

            await sut.NotifyPopularStreams();
            await sut.NotifyPopularStreams();

            notificationHandler.Received(expectedNotificationCount).AddNotification(Arg.Any<LivestreamNotification>());
        }

        private void SetupApiClient(IApiClient apiClient, List<LivestreamQueryResult> livestreamQueryResults)
        {
            if (livestreamQueryResults.Count < 3)
                throw new InvalidOperationException("Require >= 3 livestream query results for test configuration");

            livestreamQueryResults.ForEach(x => x.FailedQueryException = null); // make sure the queries are treated as successful queries
            SetViewerCount(livestreamQueryResults[0].LivestreamModel, 1000);
            SetViewerCount(livestreamQueryResults[1].LivestreamModel, 300);
            SetViewerCount(livestreamQueryResults[2].LivestreamModel, 0);
            apiClient.HasTopStreamsSupport.Returns(true);
            apiClient.GetTopStreams(Arg.Any<TopStreamQuery>()).Returns(Task.FromResult(livestreamQueryResults));
        }

        private void SetViewerCount(LivestreamModel livestreamModel, int viewers)
        {
            livestreamModel.Viewers = viewers;
            if (viewers <= 0) livestreamModel.Offline();
        }
    }
}
