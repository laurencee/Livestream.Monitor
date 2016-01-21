using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Caliburn.Micro;
using Livestream.Monitor.Core;
using Livestream.Monitor.Core.UI;
using Livestream.Monitor.Model;
using Livestream.Monitor.Model.Monitoring;
using Livestream.Monitor.Model.StreamProviders;
using TwitchTv;
using TwitchTv.Query;

namespace Livestream.Monitor.ViewModels
{
    public class TopTwitchStreamsViewModel : PagingConductor<TwitchSearchStreamResult>
    {
        private const int STREAM_TILES_PER_PAGE = 15;

        private readonly IMonitorStreamsModel monitorStreamsModel;
        private readonly ISettingsHandler settingsHandler;
        private readonly StreamLauncher streamLauncher;
        private readonly INavigationService navigationService;
        private readonly IStreamProviderFactory streamProviderFactory;

        private readonly ITwitchTvReadonlyClient twitchTvClient;
        private bool loadingItems;
        private string gameName;
        private BindableCollection<string> possibleGameNames = new BindableCollection<string>();
        private bool showPossibleGames;

        #region Design time constructor

        public TopTwitchStreamsViewModel()
        {
            if (!Execute.InDesignMode)
                throw new InvalidOperationException("Constructor only accessible from design time");

            var designTimeItems = new List<TwitchSearchStreamResult>(new[]
            {
                new TwitchSearchStreamResult
                {
                    IsMonitored = false,
                    LivestreamModel = new LivestreamModel
                    {
                        DisplayName = "Bob Ross",
                        Game = "Creative",
                        Description = "Beat the devil out of it",
                        Live = true,
                        StartTime = DateTimeOffset.Now.AddHours(-3),
                        Viewers = 50000
                    }
                }
            });

            for (var i = 0; i < 9; i++)
            {
                var stream = new TwitchSearchStreamResult();
                stream.IsMonitored = i % 3 == 0;
                stream.LivestreamModel = new LivestreamModel
                {
                    Description = "Design time item " + i,
                    DisplayName = "Display Name " + i,
                    Game = "Random Game " + i,
                    Live = true,
                    StartTime = DateTimeOffset.Now.AddMinutes(-29 - i),
                    Viewers = 30000 - i * 200
                };
                designTimeItems.Add(stream);
            }

            Items.AddRange(designTimeItems);
            ItemsPerPage = STREAM_TILES_PER_PAGE;
        }

        #endregion

        public TopTwitchStreamsViewModel(
            ITwitchTvReadonlyClient twitchTvClient,
            IMonitorStreamsModel monitorStreamsModel,
            ISettingsHandler settingsHandler,
            StreamLauncher streamLauncher,
            INavigationService navigationService,
            IStreamProviderFactory streamProviderFactory)
        {
            if (twitchTvClient == null) throw new ArgumentNullException(nameof(twitchTvClient));
            if (monitorStreamsModel == null) throw new ArgumentNullException(nameof(monitorStreamsModel));
            if (settingsHandler == null) throw new ArgumentNullException(nameof(settingsHandler));
            if (streamLauncher == null) throw new ArgumentNullException(nameof(streamLauncher));
            if (navigationService == null) throw new ArgumentNullException(nameof(navigationService));
            if (streamProviderFactory == null) throw new ArgumentNullException(nameof(streamProviderFactory));

            this.twitchTvClient = twitchTvClient;
            this.monitorStreamsModel = monitorStreamsModel;
            this.settingsHandler = settingsHandler;
            this.streamLauncher = streamLauncher;
            this.navigationService = navigationService;
            this.streamProviderFactory = streamProviderFactory;

            ItemsPerPage = STREAM_TILES_PER_PAGE;
            PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName == nameof(Page))
                {
                    NotifyOfPropertyChange(() => CanPrevious);
                }
            };
        }

        public override bool CanPrevious => Page > 1 && !LoadingItems;

        public override bool CanNext => !LoadingItems && Items.Count == STREAM_TILES_PER_PAGE;

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

        public string GameName
        {
            get { return gameName; }
            set
            {
                if (value == gameName) return;
                gameName = value;
                NotifyOfPropertyChange(() => GameName);
                if (!PossibleGameNames.Any(x => x.IsEqualTo(gameName))) UpdatePossibleGameNames();
                MovePage();
            }
        }

        public BindableCollection<string> PossibleGameNames
        {
            get { return possibleGameNames; }
            set
            {
                if (Equals(value, possibleGameNames)) return;
                possibleGameNames = value;
                NotifyOfPropertyChange(() => PossibleGameNames);
            }
        }

        public bool ShowPossibleGames
        {
            get { return showPossibleGames; }
            set
            {
                if (value == showPossibleGames) return;
                showPossibleGames = value;
                NotifyOfPropertyChange(() => ShowPossibleGames);
            }
        }
        
        public void OpenStream(TwitchSearchStreamResult stream)
        {
            if (stream == null) return;

            streamLauncher.OpenStream(stream.LivestreamModel);
        }

        public async Task OpenChat(TwitchSearchStreamResult stream)
        {
            if (stream == null) return;

            await streamLauncher.OpenChat(stream.LivestreamModel, this);
        }

        public void GotoVodViewer(TwitchSearchStreamResult stream)
        {
            if (stream?.LivestreamModel == null) return;

            navigationService.NavigateTo<VodListViewModel>(vm => vm.StreamName = stream.LivestreamModel.Id);
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

        protected override async void OnViewLoaded(object view)
        {
            if (Execute.InDesignMode) return;

            await EnsureItems();
            base.OnViewLoaded(view);
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

                var topStreamsQuery = new TopStreamQuery
                {
                    GameName = GameName,
                    Skip = (Page - 1) * ItemsPerPage,
                    Take = ItemsPerPage,
                };
                var topStreams = await twitchTvClient.GetTopStreams(topStreamsQuery);
                var monitoredStreams = monitorStreamsModel.Livestreams;

                var twitchStreams = new List<TwitchSearchStreamResult>();
                foreach (var topStream in topStreams)
                {
                    var twitchStream = new TwitchSearchStreamResult();
                    twitchStream.IsMonitored = monitoredStreams.Any(x => x.Id == topStream.Channel?.Name);
                    twitchStream.LivestreamModel.PopulateWithStreamDetails(topStream, streamProviderFactory.Get<TwitchStreamProvider>());
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

        private async void UpdatePossibleGameNames()
        {
            var game = GameName; // store local variable in case GameName changes while this is running
            if (string.IsNullOrWhiteSpace(game)) return;

            try
            {
                var games = await twitchTvClient.SearchGames(game);
                PossibleGameNames.Clear();
                PossibleGameNames.AddRange(games.Select(x => x.Name).ToList());
                ShowPossibleGames = true;
            }
            catch
            {
                // make sure we dont crash just updating auto-completion options
            }
        }
    }
}