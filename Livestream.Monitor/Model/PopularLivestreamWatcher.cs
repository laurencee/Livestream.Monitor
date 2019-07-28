using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.Caching;
using System.Threading.Tasks;
using ExternalAPIs.TwitchTv.V3.Query;
using Livestream.Monitor.Core;
using Livestream.Monitor.Model.ApiClients;
using Livestream.Monitor.Model.Monitoring;
using Livestream.Monitor.ViewModels;

namespace Livestream.Monitor.Model
{
    /// <summary>  Watches for popular livestreams/special events and notifies the user that something might going on  </summary>
    public class PopularLivestreamWatcher
    {
        private const int PollMs = 30000;
        
        private readonly ISettingsHandler settingsHandler;
        private readonly INotificationHandler notificationHandler;
        private readonly IApiClientFactory apiClientFactory;
        private readonly MemoryCache notifiedEvents = MemoryCache.Default;
        private readonly Action<IMonitorStreamsModel, LivestreamNotification> clickAction;

        private bool watching = true;
        private bool stoppedWatching;
        private int minimumEventViewers;
        
        public PopularLivestreamWatcher(
            ISettingsHandler settingsHandler,
            INotificationHandler notificationHandler,
            INavigationService navigationService,
            IMonitorStreamsModel monitorStreamsModel,
            IApiClientFactory apiClientFactory)
        {
            if (settingsHandler == null) throw new ArgumentNullException(nameof(settingsHandler));
            if (notificationHandler == null) throw new ArgumentNullException(nameof(notificationHandler));
            if (navigationService == null) throw new ArgumentNullException(nameof(navigationService));
            if (monitorStreamsModel == null) throw new ArgumentNullException(nameof(monitorStreamsModel));
            if (apiClientFactory == null) throw new ArgumentNullException(nameof(apiClientFactory));
            
            this.settingsHandler = settingsHandler;
            this.notificationHandler = notificationHandler;
            this.apiClientFactory = apiClientFactory;

            clickAction = (model, notification) =>
            {
                var livestream = model.Livestreams.FirstOrDefault(x => Equals(x, notification.LivestreamModel));
                if (livestream != null)
                    model.SelectedLivestream = livestream;
                else
                    navigationService.NavigateTo<TopStreamsViewModel>(viewModel => viewModel.SelectedApiClient = notification.LivestreamModel.ApiClient);
            };

            settingsHandler.Settings.PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName == nameof(Settings.MinimumEventViewers))
                {
                    MinimumEventViewers = settingsHandler.Settings.MinimumEventViewers;

                    if (MinimumEventViewers == 0)
                        watching = false;
                    else if (stoppedWatching)
                        StartWatching();
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

        public ObservableCollection<string> ExcludedGames { get; } = new ObservableCollection<string>();

        /// <summary> Minimum event viewers before notifications occur, set to 0 to disable notifications </summary>
        public int MinimumEventViewers
        {
            get { return minimumEventViewers; }
            private set
            {
                if (value < 0) value = 0;
                minimumEventViewers = value;
            }
        }

        /// <summary> Start watching for popular livestreams/events </summary>
        public void StartWatching()
        {
            watching = true;
            stoppedWatching = false;

            MinimumEventViewers = settingsHandler.Settings.MinimumEventViewers;
            if (!watching || MinimumEventViewers == 0) return;

            Task.Run(async () =>
            {
                while (watching)
                {
                    await NotifyPopularStreams();
                    await Task.Delay(PollMs);
                }

                stoppedWatching = true;
            });
        }

        public void StopWatching()
        {
            watching = false;
        }

        public async Task NotifyPopularStreams()
        {
            if (MinimumEventViewers == 0) return;

            var livestreamModels = await GetPopularStreams();

            foreach (var stream in livestreamModels)
            {
                // don't notify about the same event again within the next hour
                if (notifiedEvents.Get(stream.UniqueStreamKey.ToString()) != null)
                    continue;

                notifiedEvents.Set(stream.UniqueStreamKey.ToString(), stream, DateTimeOffset.Now.AddHours(1));

                stream.SetLivestreamNotifyState(settingsHandler.Settings);
                notificationHandler.AddNotification(new LivestreamNotification()
                {
                    LivestreamModel = stream,
                    ImageUrl = stream.ThumbnailUrls?.Small,
                    Message = stream.Description,
                    Title = $"[POPULAR {stream.Viewers.ToString("N0")} Viewers]\n{stream.DisplayName}",
                    Duration = LivestreamNotification.MaxDuration,
                    ClickAction = clickAction
                });
            }
        }

        private async Task<HashSet<LivestreamModel>> GetPopularStreams()
        {
            const int maxReturnCount = 5;

            var popularStreams = new HashSet<LivestreamModel>();
            int requeries = 0;
            try
            {
                var supportApiClients = apiClientFactory.GetAll().Where(x => x.HasTopStreamsSupport).ToList();
                while (popularStreams.Count < maxReturnCount && requeries < 3)
                {
                    var topStreamsQuery = new TopStreamQuery() { Skip = requeries * maxReturnCount, Take = maxReturnCount};

                    var possibleStreams = new List<LivestreamModel>();
                    foreach (var apiClient in supportApiClients)
                    {
                        var queryResults = await apiClient.GetTopStreams(topStreamsQuery);
                        possibleStreams.AddRange(queryResults.Where(x => x.IsSuccess).Select(x => x.LivestreamModel));
                    }
                    
                    // perform this check before further filtering since this is the most important check
                    if (possibleStreams.All(x => x.Viewers < MinimumEventViewers)) break;

                    popularStreams.UnionWith(
                        possibleStreams.Where(x =>
                                              x.Viewers >= MinimumEventViewers &&
                                              !ExcludedGames.Contains(x.Game) &&
                                              !notifiedEvents.Contains(x.UniqueStreamKey.ToString()) 
                            ));

                    requeries++;
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
