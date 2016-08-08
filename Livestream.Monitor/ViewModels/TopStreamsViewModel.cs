using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Caliburn.Micro;
using ExternalAPIs.TwitchTv.Query;
using Livestream.Monitor.Core;
using Livestream.Monitor.Core.UI;
using Livestream.Monitor.Model;
using Livestream.Monitor.Model.ApiClients;
using Livestream.Monitor.Model.Monitoring;

namespace Livestream.Monitor.ViewModels
{
    public class TopStreamsViewModel : PagingConductor<TopStreamResult>
    {
        private const int STREAM_TILES_PER_PAGE = 15;

        private readonly IMonitorStreamsModel monitorStreamsModel;
        private readonly ISettingsHandler settingsHandler;
        private readonly StreamLauncher streamLauncher;
        private readonly INavigationService navigationService;
        private readonly IApiClientFactory apiClientFactory;
        
        private bool loadingItems;
        private string gameName;
        private BindableCollection<string> possibleGameNames = new BindableCollection<string>();
        private bool expandPossibleGames;
        private IApiClient selectedApiClient;

        #region Design time constructor

        public TopStreamsViewModel()
        {
            if (!Execute.InDesignMode)
                throw new InvalidOperationException("Constructor only accessible from design time");

            ItemsPerPage = STREAM_TILES_PER_PAGE;
        }

        #endregion

        public TopStreamsViewModel(
            IMonitorStreamsModel monitorStreamsModel,
            ISettingsHandler settingsHandler,
            StreamLauncher streamLauncher,
            INavigationService navigationService,
            IApiClientFactory apiClientFactory)
        {
            if (monitorStreamsModel == null) throw new ArgumentNullException(nameof(monitorStreamsModel));
            if (settingsHandler == null) throw new ArgumentNullException(nameof(settingsHandler));
            if (streamLauncher == null) throw new ArgumentNullException(nameof(streamLauncher));
            if (navigationService == null) throw new ArgumentNullException(nameof(navigationService));
            if (apiClientFactory == null) throw new ArgumentNullException(nameof(apiClientFactory));
            
            this.monitorStreamsModel = monitorStreamsModel;
            this.settingsHandler = settingsHandler;
            this.streamLauncher = streamLauncher;
            this.navigationService = navigationService;
            this.apiClientFactory = apiClientFactory;

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

        public bool CanRefreshItems => !LoadingItems;

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
                NotifyOfPropertyChange(() => CanRefreshItems);
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

        public bool ExpandPossibleGames
        {
            get { return expandPossibleGames; }
            set
            {
                if (value == expandPossibleGames) return;
                expandPossibleGames = value;
                NotifyOfPropertyChange(() => ExpandPossibleGames);
            }
        }

        public BindableCollection<IApiClient> ApiClients { get; set; }

        public IApiClient SelectedApiClient
        {
            get { return selectedApiClient; }
            set
            {
                if (Equals(value, selectedApiClient)) return;
                selectedApiClient = value;
                NotifyOfPropertyChange(() => SelectedApiClient);
                MovePage();
            }
        }

        public async Task RefreshItems()
        {
            await EnsureItems();
        }

        public void OpenStream(TopStreamResult stream)
        {
            if (stream == null) return;

            streamLauncher.OpenStream(stream.LivestreamModel, monitorStreamsModel.SelectedStreamQuality);
        }

        public async Task OpenChat(TopStreamResult stream)
        {
            if (stream == null) return;

            await streamLauncher.OpenChat(stream.LivestreamModel, this);
        }

        public void GotoVodViewer(TopStreamResult stream)
        {
            if (stream?.LivestreamModel == null) return;

            navigationService.NavigateTo<VodListViewModel>(vm =>
            {
                vm.StreamId = stream.LivestreamModel.Id;
                vm.SelectedApiClient = stream.LivestreamModel.ApiClient;
            });
        }

        public void ToggleNotify(TopStreamResult stream)
        {
            if (stream == null) return;

            stream.LivestreamModel.DontNotify = !stream.LivestreamModel.DontNotify;
            var excludeNotify = stream.LivestreamModel.ToExcludeNotify();
            if (settingsHandler.Settings.ExcludeFromNotifying.Any(x => Equals(x, excludeNotify)))
            {
                settingsHandler.Settings.ExcludeFromNotifying.Remove(excludeNotify);
            }
            else
            {
                settingsHandler.Settings.ExcludeFromNotifying.Add(excludeNotify);
            }
        }

        public async Task StreamClicked(TopStreamResult topStreamResult)
        {
            if (topStreamResult.IsBusy) return;
            topStreamResult.IsBusy = true;

            if (topStreamResult.IsMonitored)
            {
                await UnmonitorStream(topStreamResult);
            }
            else
            {
                await MonitorStream(topStreamResult);
            }

            topStreamResult.IsBusy = false;
        }

        protected override void OnInitialize()
        {
            ApiClients = new BindableCollection<IApiClient>(apiClientFactory.GetAll().Where(x => x.HasTopStreamsSupport));
            if (SelectedApiClient == null)
                SelectedApiClient = apiClientFactory.Get<TwitchApiClient>();
            base.OnInitialize();
        }

        protected override async void OnViewLoaded(object view)
        {
            if (Execute.InDesignMode) return;

            await EnsureItems();
            try
            {
                var games = await apiClientFactory.Get<TwitchApiClient>().GetKnownGameNames(null);
                PossibleGameNames.Clear();
                PossibleGameNames.AddRange(games.Select(x => x.GameName));
            }
            catch { }
            
            base.OnViewLoaded(view);
        }

        private async Task UnmonitorStream(TopStreamResult topStreamResult)
        {
            try
            {
                var livestreamModel = monitorStreamsModel.Livestreams.FirstOrDefault(x => Equals(x, topStreamResult.LivestreamModel));

                if (livestreamModel != null)
                {
                    await monitorStreamsModel.RemoveLivestream(livestreamModel.ChannelIdentifier);
                }
                topStreamResult.IsMonitored = false;
            }
            catch (Exception ex)
            {
                await this.ShowMessageAsync("Error Removing Livestream", ex.Message);
            }
        }

        private async Task MonitorStream(TopStreamResult topStreamResult)
        {
            try
            {
                await monitorStreamsModel.AddLivestream(topStreamResult.ChannelIdentifier);
                topStreamResult.IsMonitored = true;
            }
            catch (Exception ex)
            {
                await this.ShowMessageAsync("Error Adding Livestream", ex.Message);
            }
        }

        protected override async void MovePage()
        {
            if (!IsActive) return;

            await EnsureItems();
            base.MovePage();
        }

        // Makes sure the items collection is populated with items for the current page
        private async Task EnsureItems()
        {
            if (!IsActive) return;

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
                var topStreams = await SelectedApiClient.GetTopStreams(topStreamsQuery);
                var monitoredStreams = monitorStreamsModel.Livestreams;

                var topStreamResults = new List<TopStreamResult>();
                foreach (var topLivestream in topStreams)
                {
                    var topStreamResult = new TopStreamResult(topLivestream.LivestreamModel, topLivestream.ChannelIdentifier);
                    topStreamResult.IsMonitored = monitoredStreams.Any(x => Equals(x, topLivestream.LivestreamModel));
                    topStreamResult.LivestreamModel.SetLivestreamNotifyState(settingsHandler.Settings);

                    topStreamResults.Add(topStreamResult);
                }

                Items.AddRange(topStreamResults);
            }
            catch (Exception ex)
            {
                await this.ShowMessageAsync("Error",
                    $"Error getting top streams from '{SelectedApiClient.ApiName}'.{Environment.NewLine}{Environment.NewLine}{ex}");
            }

            LoadingItems = false;
        }

        private async void UpdatePossibleGameNames()
        {
            var game = GameName; // store local variable in case GameName changes while this is running
            if (string.IsNullOrWhiteSpace(game)) return;

            try
            {
                var games = await SelectedApiClient.GetKnownGameNames(game);
                PossibleGameNames.Clear();
                PossibleGameNames.AddRange(games.Select(x => x.GameName));
                ExpandPossibleGames = true;
            }
            catch
            {
                // make sure we dont crash just updating auto-completion options
            }
        }
    }
}