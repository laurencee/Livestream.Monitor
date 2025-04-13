using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Caching;
using System.Threading;
using System.Threading.Tasks;
using ExternalAPIs;
using Livestream.Monitor.Core;
using Livestream.Monitor.Model.ApiClients;
using Livestream.Monitor.Model.Monitoring;
using Livestream.Monitor.ViewModels;

namespace Livestream.Monitor.Model
{
    /// <summary>  Watches for popular livestreams/special events and notifies the user that something might going on  </summary>
    public class PopularLivestreamWatcher
    {
        private const int PollMs = 60000;
        
        private readonly ISettingsHandler settingsHandler;
        private readonly INotificationHandler notificationHandler;
        private readonly IApiClientFactory apiClientFactory;
        private readonly MemoryCache notifiedEvents = MemoryCache.Default;
        private readonly Action<IMonitorStreamsModel, LivestreamNotification> clickAction;
        private readonly CancellationTokenSource cancellationTokenSource = new();

        private bool watching;
        
        public PopularLivestreamWatcher(
            ISettingsHandler settingsHandler,
            INotificationHandler notificationHandler,
            INavigationService navigationService,
            IApiClientFactory apiClientFactory)
        {
            if (navigationService == null) throw new ArgumentNullException(nameof(navigationService));

            this.settingsHandler = settingsHandler ?? throw new ArgumentNullException(nameof(settingsHandler));
            this.notificationHandler = notificationHandler ?? throw new ArgumentNullException(nameof(notificationHandler));
            this.apiClientFactory = apiClientFactory ?? throw new ArgumentNullException(nameof(apiClientFactory));

            clickAction = (model, notification) =>
            {
                var livestream = model.Livestreams.FirstOrDefault(x => Equals(x, notification.LivestreamModel));
                if (livestream != null)
                    model.SelectedLivestream = livestream;
                else
                    navigationService.NavigateTo<TopStreamsViewModel>(viewModel => viewModel.SelectedApiClient = notification.LivestreamModel.ApiClient);
            };

            settingsHandler.Settings.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(Settings.MinimumEventViewers))
                {
                    if (!watching && PopularEventNotificationEnabled) StartWatching();
                }
                else if (args.PropertyName == nameof(Settings.DisableNotifications))
                {
                    if (!settingsHandler.Settings.DisableNotifications)
                    {
                        // clear the existing cache so the next poll can re-add notifications
                        notifiedEvents.ToList().ForEach(x => notifiedEvents.Remove(x.Key));
                    }
                }
            };
        }

        private bool PopularEventNotificationEnabled => settingsHandler.Settings.MinimumEventViewers > 0;

        /// <summary> Start watching for popular livestreams/events </summary>
        public void StartWatching()
        {
            if (!PopularEventNotificationEnabled) return;

            watching = true;
            Task.Run(async () =>
            {
                while (watching && PopularEventNotificationEnabled)
                {
                    await NotifyPopularStreams();
                    await Task.Delay(PollMs);
                }
            }, cancellationTokenSource.Token).ContinueWith(_ => {}, TaskContinuationOptions.OnlyOnCanceled); // ignore cancellations
        }

        public void StopWatching()
        {
            watching = false;
            cancellationTokenSource.Cancel();
        }

        public async Task NotifyPopularStreams()
        {
            if (!PopularEventNotificationEnabled)
            {
                watching = false;
                return;
            }

            var livestreamModels = await GetPopularStreams();
            foreach (var livestreamModel in livestreamModels)
            {
                // don't notify about the same event again within the next hour
                if (notifiedEvents.Get(livestreamModel.UniqueStreamKey.ToString()) != null)
                    continue;

                notifiedEvents.Set(livestreamModel.UniqueStreamKey.ToString(), livestreamModel, DateTimeOffset.Now.AddHours(1));

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

        private async Task<HashSet<LivestreamModel>> GetPopularStreams()
        {
            const int maxReturnCount = 5;
            var popularStreams = new HashSet<LivestreamModel>();

            var supportedApiClients = apiClientFactory.GetAll()
                .Where(x => x.HasTopStreamsSupport && x.IsAuthorized)
                .ToArray();
            if (supportedApiClients.Length == 0) return popularStreams;

            const int maxQueryCount = 3;
            int queryCount = 0;
            try
            {
                while (popularStreams.Count < maxReturnCount && queryCount < maxQueryCount)
                {
                    var topStreamsQuery = new TopStreamQuery()
                    {
                        Skip = queryCount * maxReturnCount,
                        Take = maxReturnCount,
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
                            !notifiedEvents.Contains(x.UniqueStreamKey.ToString()) &&
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
