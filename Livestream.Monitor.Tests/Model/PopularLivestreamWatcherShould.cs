using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
            [Frozen] INavigationService navigationService)
        {
            var apiClient = apiClients.First();
            SetupApiClient(apiClient, livestreamModels);
            factory.GetAll().Returns(apiClients);
            settingsHandler.Settings.MinimumEventViewers = minimumViewers;
            var sut = new PopularLivestreamWatcher(settingsHandler, notificationHandler, navigationService, factory);

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
            [Frozen] INavigationService navigationService)
        {
            var apiClient = apiClients.First();
            SetupApiClient(apiClient, livestreamModels);
            factory.GetAll().Returns(apiClients);
            settingsHandler.Settings.MinimumEventViewers = 2000;
            var sut = new PopularLivestreamWatcher(settingsHandler, notificationHandler, navigationService, factory);

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
            [Frozen] INavigationService navigationService)
        {
            var apiClient = apiClients.First();
            SetupApiClient(apiClient, livestreamModels);
            factory.GetAll().Returns(apiClients);
            settingsHandler.Settings.MinimumEventViewers = minimumViewers;
            var sut = new PopularLivestreamWatcher(settingsHandler, notificationHandler, navigationService, factory);

            await sut.NotifyPopularStreams();
            await sut.NotifyPopularStreams();

            notificationHandler.Received(expectedNotificationCount).AddNotification(Arg.Any<LivestreamNotification>());
        }

        [Theory, AutoNSubstituteData]
        public async Task NotNotifyExcludedStreams(
            [Frozen] ISettingsHandler settingsHandler,
            [Frozen] INotificationHandler notificationHandler,
            [Frozen] List<IApiClient> apiClients,
            [Frozen] List<LivestreamModel> livestreamModels,
            [Frozen] IApiClientFactory factory,
            [Frozen] INavigationService navigationService)
        {
            var apiClient = apiClients.First();
            SetupApiClient(apiClient, livestreamModels);
            factory.GetAll().Returns(apiClients);
            settingsHandler.Settings.MinimumEventViewers = 1;
            settingsHandler.Settings.ExcludeFromNotifying.Add(livestreamModels[0].ToExcludeNotify());
            var sut = new PopularLivestreamWatcher(settingsHandler, notificationHandler, navigationService, factory);

            await sut.NotifyPopularStreams();

            notificationHandler.Received(1).AddNotification(
                Arg.Is<LivestreamNotification>(x => Equals(x.LivestreamModel, livestreamModels[1])));
            notificationHandler.DidNotReceive().AddNotification(
                Arg.Is<LivestreamNotification>(x => Equals(x.LivestreamModel, livestreamModels[0])));
        }

        [Theory, AutoNSubstituteData]
        public async Task StartWatching_DoesNothing_WhenPopularNotificationsDisabled(
            [Frozen] ISettingsHandler settingsHandler,
            [Frozen] INotificationHandler notificationHandler,
            [Frozen] IApiClient apiClient,
            [Frozen] IApiClientFactory factory,
            [Frozen] INavigationService navigationService)
        {
            int queryCount = 0;
            SetupPollingApiClient(apiClient, () => Interlocked.Increment(ref queryCount));
            factory.GetAll().Returns([apiClient]);
            settingsHandler.Settings.MinimumEventViewers = 0;
            var sut = new PopularLivestreamWatcher(settingsHandler, notificationHandler, navigationService, factory);

            try
            {
                sut.StartWatching();
                await AssertRemainsTrueAsync(() => Volatile.Read(ref queryCount) == 0);
            }
            finally
            {
                sut.StopWatching();
            }
        }

        [Theory, AutoNSubstituteData]
        public async Task StartWatching_IsIdempotent_AndTriggersOneImmediateQuery(
            [Frozen] ISettingsHandler settingsHandler,
            [Frozen] INotificationHandler notificationHandler,
            [Frozen] IApiClient apiClient,
            [Frozen] IApiClientFactory factory,
            [Frozen] INavigationService navigationService)
        {
            int queryCount = 0;
            SetupPollingApiClient(apiClient, () => Interlocked.Increment(ref queryCount));
            factory.GetAll().Returns([apiClient]);
            settingsHandler.Settings.MinimumEventViewers = 1;
            var sut = new PopularLivestreamWatcher(settingsHandler, notificationHandler, navigationService, factory);

            try
            {
                sut.StartWatching();
                sut.StartWatching();

                await WaitUntilAsync(() => Volatile.Read(ref queryCount) == 1);
                await AssertRemainsTrueAsync(() => Volatile.Read(ref queryCount) == 1);
            }
            finally
            {
                sut.StopWatching();
            }
        }

        [Theory, AutoNSubstituteData]
        public async Task StopWatching_AllowsImmediateRestart(
            [Frozen] ISettingsHandler settingsHandler,
            [Frozen] INotificationHandler notificationHandler,
            [Frozen] IApiClient apiClient,
            [Frozen] IApiClientFactory factory,
            [Frozen] INavigationService navigationService)
        {
            int queryCount = 0;
            SetupPollingApiClient(apiClient, () => Interlocked.Increment(ref queryCount));
            factory.GetAll().Returns([apiClient]);
            settingsHandler.Settings.MinimumEventViewers = 1;
            var sut = new PopularLivestreamWatcher(settingsHandler, notificationHandler, navigationService, factory);

            try
            {
                sut.StartWatching();
                await WaitUntilAsync(() => Volatile.Read(ref queryCount) == 1);

                sut.StopWatching();
                sut.StartWatching();

                await WaitUntilAsync(() => Volatile.Read(ref queryCount) == 2);
            }
            finally
            {
                sut.StopWatching();
            }
        }

        [Theory, AutoNSubstituteData]
        public async Task ChangingMinimumEventViewers_StopsAndRestartsWatching(
            [Frozen] ISettingsHandler settingsHandler,
            [Frozen] INotificationHandler notificationHandler,
            [Frozen] IApiClient apiClient,
            [Frozen] IApiClientFactory factory,
            [Frozen] INavigationService navigationService)
        {
            int queryCount = 0;
            SetupPollingApiClient(apiClient, () => Interlocked.Increment(ref queryCount));
            factory.GetAll().Returns([apiClient]);
            settingsHandler.Settings.MinimumEventViewers = 0;
            var sut = new PopularLivestreamWatcher(settingsHandler, notificationHandler, navigationService, factory);

            try
            {
                settingsHandler.Settings.MinimumEventViewers = 1;
                await WaitUntilAsync(() => Volatile.Read(ref queryCount) == 1);

                settingsHandler.Settings.MinimumEventViewers = 0;
                settingsHandler.Settings.MinimumEventViewers = 1;

                await WaitUntilAsync(() => Volatile.Read(ref queryCount) == 2);
            }
            finally
            {
                sut.StopWatching();
            }
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

        private void SetupPollingApiClient(IApiClient apiClient, Action onQuery)
        {
            apiClient.HasTopStreamsSupport.Returns(true);
            apiClient.IsAuthorized.Returns(true);
            apiClient.GetTopStreams(Arg.Any<TopStreamQuery>()).Returns(_ =>
            {
                onQuery();
                return Task.FromResult(new TopStreamsResponse()
                {
                    LivestreamModels = [],
                    HasNextPage = false,
                });
            });
        }

        private static async Task WaitUntilAsync(Func<bool> predicate, int timeoutMs = 100)
        {
            var timeout = DateTimeOffset.UtcNow.AddMilliseconds(timeoutMs);
            while (!predicate())
            {
                if (DateTimeOffset.UtcNow >= timeout)
                    throw new TimeoutException("Timed out waiting for watcher state change");

                await Task.Delay(1);
            }
        }

        private static async Task AssertRemainsTrueAsync(Func<bool> predicate, int timeoutMs = 50)
        {
            var timeout = DateTimeOffset.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTimeOffset.UtcNow < timeout)
            {
                if (!predicate())
                    throw new Xunit.Sdk.XunitException("Watcher changed state unexpectedly");

                await Task.Delay(1);
            }
        }
    }
}
