using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExternalAPIs;
using Livestream.Monitor.Core;
using Livestream.Monitor.Core.Settings;
using Livestream.Monitor.Model.ApiClients;
using Livestream.Monitor.Model.Monitoring;

namespace Livestream.Monitor.Model
{
    /// <summary>  Watches for popular livestreams/special events and notifies the user that something might going on  </summary>
    public class PopularLivestreamWatcher
    {
        private const int PollMs = 60000;

        private readonly ISettingsHandler settingsHandler;
        private readonly INotificationHandler notificationHandler;
        private readonly IApiClientFactory apiClientFactory;
        private readonly ConcurrentDictionary<UniqueStreamKey, DateTimeOffset> notifiedEvents = new();
        private readonly Action<IMonitorStreamsModel, LivestreamNotification> clickAction;
        private readonly object watchLock = new();

        private CancellationTokenSource cancellationTokenSource;

        public PopularLivestreamWatcher(
            ISettingsHandler settingsHandler,
            INotificationHandler notificationHandler,
            Action<LivestreamModel> openStream,
            IApiClientFactory apiClientFactory)
        {
            ArgumentNullException.ThrowIfNull(openStream);

            this.settingsHandler = settingsHandler ?? throw new ArgumentNullException(nameof(settingsHandler));
            this.notificationHandler = notificationHandler ?? throw new ArgumentNullException(nameof(notificationHandler));
            this.apiClientFactory = apiClientFactory ?? throw new ArgumentNullException(nameof(apiClientFactory));

            clickAction = (model, notification) =>
            {
                var livestream = model.Livestreams.FirstOrDefault(x => Equals(x, notification.LivestreamModel));
                if (livestream != null)
                    model.SelectedLivestream = livestream;
                else
                    openStream(notification.LivestreamModel);
            };

            settingsHandler.Settings.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(Settings.MinimumEventViewers))
                {
                    if (PopularEventNotificationEnabled)
                        StartWatching();
                    else
                        StopWatching();
                }
                else if (args.PropertyName == nameof(Settings.DisableNotifications))
                {
                    if (!settingsHandler.Settings.DisableNotifications)
                    {
                        // clear the existing cache so the next poll can re-add notifications
                        notifiedEvents.Clear();
                    }
                }
            };
        }

        private bool PopularEventNotificationEnabled => settingsHandler.Settings.MinimumEventViewers > 0;

        /// <summary> Start watching for popular livestreams/events </summary>
        public void StartWatching()
        {
            if (!PopularEventNotificationEnabled) return;

            CancellationTokenSource localCancellationTokenSource;
            lock (watchLock)
            {
                if (cancellationTokenSource != null) return;

                localCancellationTokenSource = new CancellationTokenSource();
                cancellationTokenSource = localCancellationTokenSource;
            }

            Task.Run(async () =>
            {
                try
                {
                    while (!localCancellationTokenSource.IsCancellationRequested)
                    {
                        await NotifyPopularStreams();
                        await Task.Delay(PollMs, localCancellationTokenSource.Token);
                    }
                }
                catch (OperationCanceledException) { } // ignore
                finally
                {
                    lock (watchLock)
                    {
                        if (ReferenceEquals(cancellationTokenSource, localCancellationTokenSource))
                        {
                            cancellationTokenSource = null;
                        }
                    }

                    localCancellationTokenSource.Dispose();
                }
            }, localCancellationTokenSource.Token);
        }

        public void StopWatching()
        {
            CancellationTokenSource localCancellationTokenSource;
            lock (watchLock)
            {
                if (cancellationTokenSource == null) return;
                localCancellationTokenSource = cancellationTokenSource;
                cancellationTokenSource = null;
            }

            localCancellationTokenSource?.Cancel();
        }

        public async Task NotifyPopularStreams()
        {
            if (!PopularEventNotificationEnabled)
            {
                return;
            }

            foreach (var entry in notifiedEvents)
                if (entry.Value <= DateTimeOffset.UtcNow)
                    notifiedEvents.TryRemove(entry.Key, out _);

            var livestreamModels = await GetPopularStreams();
            foreach (var livestreamModel in livestreamModels)
            {
                // don't notify about the same event again within the next hour
                if (WasNotified(livestreamModel.UniqueStreamKey))
                    continue;

                notifiedEvents[livestreamModel.UniqueStreamKey] = DateTimeOffset.UtcNow.AddHours(1);

                livestreamModel.SetLivestreamNotifyState(settingsHandler.Settings);
                notificationHandler.AddNotification(new LivestreamNotification()
                {
                    LivestreamModel = livestreamModel,
                    ImageUrl = livestreamModel.ThumbnailUrls?.Small,
                    Message = livestreamModel.Description,
                    Title = $"[POPULAR {livestreamModel.Viewers:N0} Viewers]\n{livestreamModel.DisplayName}",
                    Duration = LivestreamNotification.MaxDuration,
                    ClickAction = clickAction,
                });
            }
        }

        private bool WasNotified(UniqueStreamKey key)
        {
            if (!notifiedEvents.TryGetValue(key, out var until)) return false;
            if (until > DateTimeOffset.UtcNow) return true;

            notifiedEvents.TryRemove(key, out _);
            return false;
        }

        private async Task<HashSet<LivestreamModel>> GetPopularStreams()
        {
            const int MaxReturnCount = 5;
            var popularStreams = new HashSet<LivestreamModel>();

            var supportedApiClients = apiClientFactory.GetAll()
                .Where(x => x.HasTopStreamsSupport && x.IsAuthorized)
                .ToArray();
            if (supportedApiClients.Length == 0) return popularStreams;

            const int MaxQueryCount = 3;
            int queryCount = 0;
            try
            {
                while (popularStreams.Count < MaxReturnCount && queryCount < MaxQueryCount)
                {
                    var topStreamsQuery = new TopStreamQuery()
                    {
                        Skip = queryCount * MaxReturnCount,
                        Take = MaxReturnCount,
                    };

                    var potentialStreams = new List<LivestreamModel>();
                    foreach (var apiClient in supportedApiClients)
                    {
                        var queryResults = await apiClient.GetTopStreams(topStreamsQuery);
                        potentialStreams.AddRange(queryResults.LivestreamModels);
                    }

                    // no future queries will ever return useful results
                    if (potentialStreams.All(x => x.Viewers < settingsHandler.Settings.MinimumEventViewers)) break;

                    // merge new entries with existing set
                    popularStreams.UnionWith(
                        potentialStreams.Where(x =>
                            x.Viewers >= settingsHandler.Settings.MinimumEventViewers &&
                            !WasNotified(x.UniqueStreamKey) &&
                            !settingsHandler.Settings.ExcludeFromNotifying.Contains(x.UniqueStreamKey)
                        ));

                    // we filtered out something but there are no more streams above the min threshold
                    if (popularStreams.Count == 0 &&
                        potentialStreams.Any(x => x.Viewers < settingsHandler.Settings.MinimumEventViewers))
                        break;

                    queryCount++;
                }
            }
            catch
            {
                // nothing we can really do here, we just wanna make sure the polling continues
            }

            return popularStreams;
        }
    }
}
