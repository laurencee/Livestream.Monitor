using System;
using System.Threading;
using System.Threading.Tasks;
using ExternalAPIs;
using Livestream.Monitor.Core.Settings;
using Livestream.Monitor.Model;
using Livestream.Monitor.Model.ApiClients;
using Livestream.Monitor.Model.Monitoring;
using NSubstitute;
using Xunit;

namespace Livestream.Monitor.Tests.Model
{
    public class PopularLivestreamWatcherShould
    {
        // Popular notifications start disabled; a test enables them when it needs polling.
        private readonly Settings settings = new() { MinimumEventViewers = 0 };
        private readonly INotificationHandler notificationHandler = Substitute.For<INotificationHandler>();
        private readonly IApiClient apiClient = Substitute.For<IApiClient>();
        private int queryCount;

        public PopularLivestreamWatcherShould()
        {
            apiClient.ApiName.Returns("twitchtv");
            apiClient.HasTopStreamsSupport.Returns(true);
            apiClient.IsAuthorized.Returns(true);
        }

        [Theory]
        [InlineData(1, false, 2)]
        [InlineData(500, false, 1)]
        [InlineData(2000, false, 0)]
        [InlineData(1, true, 1)]
        public async Task NotifyEligibleStreamsOnceAcrossConsecutivePolls(int minimumViewers, bool muteTheMostViewed, int expectedNotifications)
        {
            var mostViewed = Stream("most viewed", viewers: 1000);
            var lessViewed = Stream("less viewed", viewers: 300);
            var offline = Stream("offline", viewers: 0);
            offline.Offline();
            var response = new TopStreamsResponse { LivestreamModels = [mostViewed, lessViewed, offline], HasNextPage = false };
            apiClient.GetTopStreams(Arg.Any<TopStreamQuery>()).Returns(response);
            settings.MinimumEventViewers = minimumViewers;
            if (muteTheMostViewed) settings.ExcludeFromNotifying.Add(mostViewed.UniqueStreamKey);
            var sut = CreateWatcher();

            await sut.NotifyPopularStreams();

            notificationHandler.Received(expectedNotifications).AddNotification(Arg.Any<LivestreamNotification>());

            await sut.NotifyPopularStreams();

            notificationHandler.Received(expectedNotifications).AddNotification(Arg.Any<LivestreamNotification>());
            if (!muteTheMostViewed) return;

            notificationHandler.Received(1).AddNotification(Arg.Is<LivestreamNotification>(x => Equals(x.LivestreamModel, lessViewed)));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task PollOnceOnDuplicateStartOrAgainAfterRestart(bool restart)
        {
            CountQueries();
            settings.MinimumEventViewers = 1;
            var sut = CreateWatcher();

            try
            {
                sut.StartWatching();
                if (!restart) sut.StartWatching();

                await WaitUntilAsync(() => Volatile.Read(ref queryCount) == 1);

                if (restart)
                {
                    sut.StopWatching();
                    sut.StartWatching();
                    await WaitUntilAsync(() => Volatile.Read(ref queryCount) == 2);
                }
                else
                {
                    await AssertRemainsTrueAsync(() => Volatile.Read(ref queryCount) == 1);
                }
            }
            finally
            {
                sut.StopWatching();
            }
        }

        [Fact]
        public async Task ChangingMinimumEventViewers_StopsAndRestartsWatching()
        {
            CountQueries();
            var sut = CreateWatcher();

            try
            {
                // Starting does nothing while popular notifications are disabled.
                sut.StartWatching();
                await AssertRemainsTrueAsync(() => Volatile.Read(ref queryCount) == 0);

                settings.MinimumEventViewers = 1;
                await WaitUntilAsync(() => Volatile.Read(ref queryCount) == 1);

                settings.MinimumEventViewers = 0;
                settings.MinimumEventViewers = 1;

                await WaitUntilAsync(() => Volatile.Read(ref queryCount) == 2);
            }
            finally
            {
                sut.StopWatching();
            }
        }

        private PopularLivestreamWatcher CreateWatcher()
        {
            var settingsHandler = Substitute.For<ISettingsHandler>();
            settingsHandler.Settings.Returns(settings);
            var factory = Substitute.For<IApiClientFactory>();
            factory.GetAll().Returns([apiClient]);
            return new PopularLivestreamWatcher(settingsHandler, notificationHandler, factory);
        }

        private LivestreamModel Stream(string id, long viewers)
        {
            var channel = new ChannelIdentifier(apiClient, id);
            return new LivestreamModel(id, channel) { DisplayName = id, Live = true, Viewers = viewers };
        }

        private void CountQueries()
        {
            apiClient.GetTopStreams(Arg.Any<TopStreamQuery>()).Returns(_ =>
            {
                Interlocked.Increment(ref queryCount);
                var response = new TopStreamsResponse { LivestreamModels = [], HasNextPage = false };
                return Task.FromResult(response);
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
