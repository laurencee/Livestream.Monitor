using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Caliburn.Micro;
using Livestream.Monitor.Core;
using Livestream.Monitor.Core.UI;
using Livestream.Monitor.Model;
using Livestream.Monitor.Model.Monitoring;
using TwitchTv;
using TwitchTv.Dto;

namespace Livestream.Monitor.ViewModels
{
    public class TopTwitchStreamsViewModel : PagingConductor<TwitchSearchStreamResult>
    {
        private const int ITEMS_PER_PAGE = 15;

        private readonly ITwitchTvReadonlyClient twitchTvClient;
        private readonly IMonitorStreamsModel monitorStreamsModel;
        private readonly ISettingsHandler settingsHandler;
        private readonly StreamLauncher streamLauncher;
        private List<Stream> topStreams;
        private bool loadingItems;

        #region Design time constructor

        public TopTwitchStreamsViewModel()
        {
            if (!Execute.InDesignMode)
                throw new InvalidOperationException("Constructor only accessible from design time");

            var designTimeItems = new List<TwitchSearchStreamResult>(new[]
            {
                new TwitchSearchStreamResult()
                {
                    IsMonitored = false,
                    LivestreamModel = new LivestreamModel()
                    {
                        DisplayName = "Bob Ross",
                        Game = "Creative",
                        Description = "Beat the devil out of it",
                        Live = true,
                        StartTime = DateTimeOffset.Now.AddHours(-3),
                        Viewers = 50000
                    }
                },
            });

            for (int i = 0; i < 9; i++)
            {
                var stream = new TwitchSearchStreamResult();
                stream.IsMonitored = i % 3 == 0;
                stream.LivestreamModel = new LivestreamModel()
                {
                    Description = "Design time item " + i,
                    DisplayName = "Display Name " + i,
                    Game = "Random Game " + i,
                    Live = true,
                    StartTime = DateTimeOffset.Now.AddMinutes(-29 - i),
                    Viewers = 30000 - (i * 200)
                };
                designTimeItems.Add(stream);
            }

            Items.AddRange(designTimeItems);
            ItemsPerPage = ITEMS_PER_PAGE;
        }

        #endregion

        public TopTwitchStreamsViewModel(
            ITwitchTvReadonlyClient twitchTvClient,
            IMonitorStreamsModel monitorStreamsModel,
            ISettingsHandler settingsHandler,
            StreamLauncher streamLauncher)
        {
            if (twitchTvClient == null) throw new ArgumentNullException(nameof(twitchTvClient));
            if (monitorStreamsModel == null) throw new ArgumentNullException(nameof(monitorStreamsModel));
            if (settingsHandler == null) throw new ArgumentNullException(nameof(settingsHandler));
            if (streamLauncher == null) throw new ArgumentNullException(nameof(streamLauncher));

            this.twitchTvClient = twitchTvClient;
            this.monitorStreamsModel = monitorStreamsModel;
            this.settingsHandler = settingsHandler;
            this.streamLauncher = streamLauncher;

            ItemsPerPage = ITEMS_PER_PAGE;
            PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName == nameof(Page))
                {
                    NotifyOfPropertyChange(() => CanPrevious);
                }
            };
        }

        protected override async void OnViewLoaded(object view)
        {
            if (Execute.InDesignMode) return;

            await EnsureItems();
            base.OnViewLoaded(view);
        }

        public override bool CanPrevious => Page > 1 && !LoadingItems;

        public override bool CanNext => !LoadingItems;

        public bool LoadingItems
        {
            get { return loadingItems; }
            set
            {
                if (value == loadingItems) return;
                loadingItems = value;
                NotifyOfPropertyChange(() => LoadingItems);
                NotifyOfPropertyChange(() => CanPrevious);
                NotifyOfPropertyChange(() => CanNext);
            }
        }

        public void LaunchStream(TwitchSearchStreamResult stream)
        {
            if (stream == null) return;

            streamLauncher.StartStream(stream.LivestreamModel);
        }

        public async Task OpenChat(TwitchSearchStreamResult stream)
        {
            if (stream == null) return;

            await streamLauncher.OpenChat(stream.LivestreamModel, this);
        }

        public void ToggleNotify(TwitchSearchStreamResult stream)
        {
            if (stream == null) return;

            stream.LivestreamModel.DontNotify = !stream.LivestreamModel.DontNotify;
            if (settingsHandler.Settings.ExcludeFromNotifying.Any(x => x.IsEqualTo(stream.LivestreamModel.Id)))
            {
                settingsHandler.Settings.ExcludeFromNotifying.Remove(stream.LivestreamModel.Id);
            }
            else
            {
                settingsHandler.Settings.ExcludeFromNotifying.Add(stream.LivestreamModel.Id);
            }
        }

        public async Task StreamClicked(TwitchSearchStreamResult twitchSearchStreamResult)
        {
            if (twitchSearchStreamResult.IsBusy) return;
            twitchSearchStreamResult.IsBusy = true;

            if (twitchSearchStreamResult.IsMonitored)
            {
                await UnmonitorStream(twitchSearchStreamResult);
            }
            else
            {
                await MonitorStream(twitchSearchStreamResult);
            }

            twitchSearchStreamResult.IsBusy = false;
        }

        private async Task UnmonitorStream(TwitchSearchStreamResult twitchSearchStreamResult)
        {
            try
            {
                var livestreamModel = monitorStreamsModel.Livestreams.FirstOrDefault(x => x.Id == twitchSearchStreamResult.LivestreamModel.Id);
                if (livestreamModel != null)
                {
                    monitorStreamsModel.RemoveLivestream(livestreamModel);
                }
                twitchSearchStreamResult.IsMonitored = false;
            }
            catch (Exception ex)
            {
                await this.ShowMessageAsync("Error", "An error occured removing the stream from monitoring:" + ex.Message);
            }
        }

        private async Task MonitorStream(TwitchSearchStreamResult twitchSearchStreamResult)
        {
            try
            {
                await monitorStreamsModel.AddLivestream(twitchSearchStreamResult.LivestreamModel);
                twitchSearchStreamResult.IsMonitored = true;
            }
            catch (Exception ex)
            {
                await this.ShowMessageAsync("Error", "An error occured adding the stream for monitoring: " + ex.Message);
            }
        }

        protected override async void MovePage()
        {
            await EnsureItems();
            base.MovePage();
        }

        // Makes sure the items collection is populated with items for the current page
        private async Task EnsureItems()
        {
            LoadingItems = true;

            try
            {
                Items.Clear();

                var topStreamsQuery = new TopStreamQuery() { Skip = (Page - 1) * ItemsPerPage, Take = ItemsPerPage };
                topStreams = await twitchTvClient.GetTopStreams(topStreamsQuery);
                var monitoredStreams = monitorStreamsModel.Livestreams;

                var twitchStreams = new List<TwitchSearchStreamResult>();
                foreach (var topStream in topStreams)
                {
                    var twitchStream = new TwitchSearchStreamResult();
                    twitchStream.IsMonitored = monitoredStreams.Any(x => x.Id == topStream.Channel?.Name);
                    twitchStream.LivestreamModel.PopulateWithStreamDetails(topStream);
                    twitchStream.LivestreamModel.SetLivestreamNotifyState(settingsHandler.Settings);

                    twitchStreams.Add(twitchStream);
                }

                Items.AddRange(twitchStreams);
            }
            catch (Exception ex)
            {
                await this.ShowMessageAsync("Error",
                    $"An error occured attempting to get top twitch streams.{Environment.NewLine}{Environment.NewLine}{ex}");
            }

            LoadingItems = false;
        }
    }
}