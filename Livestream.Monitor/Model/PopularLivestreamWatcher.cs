using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.Caching;
using System.Threading.Tasks;
using Livestream.Monitor.Core;
using Livestream.Monitor.Model.Monitoring;
using Livestream.Monitor.ViewModels;
using TwitchTv;
using TwitchTv.Dto;
using TwitchTv.Query;

namespace Livestream.Monitor.Model
{
    /// <summary>  Watches for popular livestreams/special events and notifies the user that something might going on  </summary>
    public class PopularLivestreamWatcher
    {
        private const int PollMs = 30000;

        private readonly ITwitchTvReadonlyClient twitchTvClient;
        private readonly ISettingsHandler settingsHandler;
        private readonly NotificationHandler notificationHandler;
        private readonly MemoryCache notifiedEvents = MemoryCache.Default;
        private readonly Action<IMonitorStreamsModel, LivestreamNotification> clickAction;

        private bool watching = true;
        private bool stoppedWatching;
        private int minimumEventViewers;
        
        public PopularLivestreamWatcher(
            ITwitchTvReadonlyClient twitchTvClient,
            ISettingsHandler settingsHandler,
            NotificationHandler notificationHandler,
            INavigationService navigationService,
            IMonitorStreamsModel monitorStreamsModel)
        {
            if (twitchTvClient == null) throw new ArgumentNullException(nameof(twitchTvClient));
            if (settingsHandler == null) throw new ArgumentNullException(nameof(settingsHandler));
            if (notificationHandler == null) throw new ArgumentNullException(nameof(notificationHandler));
            if (navigationService == null) throw new ArgumentNullException(nameof(navigationService));
            if (monitorStreamsModel == null) throw new ArgumentNullException(nameof(monitorStreamsModel));

            this.twitchTvClient = twitchTvClient;
            this.settingsHandler = settingsHandler;
            this.notificationHandler = notificationHandler;

            clickAction = (model, notification) =>
            {
                var livestream = model.Livestreams.FirstOrDefault(x => Equals(x, notification.LivestreamModel));
                if (livestream != null)
                    model.SelectedLivestream = livestream;
                else
                    navigationService.NavigateTo<TopTwitchStreamsViewModel>();
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
                    var livestreamModels = await GetPopularStreams();

                    foreach (var stream in livestreamModels)
                    {
                        // don't notify about the same event again within the next hour
                        if (notifiedEvents.Get(stream.Id) != null)
                            continue;

                        notifiedEvents.Set(stream.Id, stream, DateTimeOffset.Now.AddHours(1));

                        stream.SetLivestreamNotifyState(settingsHandler.Settings);
                        notificationHandler.AddNotification(new LivestreamNotification()
                        {
                            LivestreamModel = stream,
                            ImageUrl = stream.PreviewImage?.Small,
                            Message = stream.Description,
                            Title = $"[POPULAR {stream.Viewers.ToString("N0")} Viewers]\n{stream.DisplayName}",
                            Duration = LivestreamNotification.MaxDuration,
                            ClickAction = clickAction
                        });
                    }

                    await Task.Delay(PollMs);
                }

                stoppedWatching = true;
            });
        }

        public void StopWatching()
        {
            watching = false;
        }

        private async Task<List<LivestreamModel>> GetPopularStreams()
        {
            const int maxReturnCount = 5;

            List<Stream> popularStreams = new List<Stream>();
            int requeries = 0;
            try
            {
                while (popularStreams.Count < maxReturnCount && requeries < 3)
                {
                    var topStreamsQuery = new TopStreamQuery() { Skip = requeries * maxReturnCount, Take = maxReturnCount};
                    var possibleStreams = await twitchTvClient.GetTopStreams(topStreamsQuery);

                    // perform this check before further filtering since this is the most important check
                    if (possibleStreams.All(x => x.Viewers < MinimumEventViewers)) break;

                    popularStreams.AddRange(
                        possibleStreams.Where(x =>
                                              x.Viewers >= MinimumEventViewers &&
                                              !ExcludedGames.Contains(x.Game) &&
                                              x.Channel?.Name != null && // prevent adding streams we've already shown recently
                                              !notifiedEvents.Contains(x.Channel?.Name)
                            ));

                    requeries++;
                }
            }
            catch
            {
                // nothing we can really do here, we just wanna make sure the polling continues
            }
            

            return popularStreams.Select(x => x.ToLivestreamModel()).ToList();
        }
    }
}
