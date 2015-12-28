using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Caching;
using System.Threading.Tasks;
using Livestream.Monitor.Core;
using Livestream.Monitor.Model.Monitoring;
using TwitchTv;
using TwitchTv.Dto;

namespace Livestream.Monitor.Model
{
    /// <summary>  Watches for popular livestreams/special events and notifies the user that something might going on  </summary>
    public class LivestreamEventWatcher
    {
        private const int PollMs = 30000;

        private readonly ITwitchTvReadonlyClient twitchTvClient;
        private readonly ISettingsHandler settingsHandler;
        private readonly NotificationHandler notificationHandler;
        private readonly MemoryCache notifiedEvents = MemoryCache.Default;

        private bool watching = true;
        private bool stoppedWatching;
        private int minimumEventViewers;
        
        public LivestreamEventWatcher(
            ITwitchTvReadonlyClient twitchTvClient,
            ISettingsHandler settingsHandler,
            NotificationHandler notificationHandler)
        {
            if (twitchTvClient == null) throw new ArgumentNullException(nameof(twitchTvClient));
            if (settingsHandler == null) throw new ArgumentNullException(nameof(settingsHandler));
            if (notificationHandler == null) throw new ArgumentNullException(nameof(notificationHandler));

            this.twitchTvClient = twitchTvClient;
            this.settingsHandler = settingsHandler;
            this.notificationHandler = notificationHandler;

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

                        notificationHandler.AddNotification(new LivestreamNotification()
                        {
                            LivestreamModel = stream,
                            ImageUrl = stream.PreviewImage?.Small,
                            Message = stream.Description,
                            Title = $"POPULAR STREAM! [{stream.DisplayName}]",
                            Duration = TimeSpan.FromSeconds(30) // keep this up for a longer time than usual notifications
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
                    var possibleStreams = await twitchTvClient.GetTopStreams(skip: requeries * maxReturnCount, take: maxReturnCount);
                    if (possibleStreams.All(x => x.Viewers < MinimumEventViewers)) break; // no events/streams we care about

                    popularStreams.AddRange(
                        possibleStreams.Where(x =>
                                              x.Viewers >= MinimumEventViewers &&
                                              !ExcludedGames.Contains(x.Game) &&
                                              x.Channel?.Name != null &&
                                              !notifiedEvents.Contains(x.Channel?.Name)
                            ));

                    requeries++;
                }
            }
            catch
            {
                // nothing we can really do here, we dont want the polling to stop and only care about the actual error while debugging...
                if (Debugger.IsAttached) throw;
            }
            

            return popularStreams.Select(x => x.ToLivestreamModel()).ToList();
        }
    }
}
