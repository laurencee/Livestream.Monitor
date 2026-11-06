using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Caliburn.Micro;
using ExternalAPIs;
using Livestream.Monitor.Core.UI;
using Livestream.Monitor.Core;
using Livestream.Monitor.Core.Settings;
using Livestream.Monitor.Model;
using Livestream.Monitor.Model.ApiClients;
using Livestream.Monitor.Model.Monitoring;
using Livestream.Monitor.WPF.Core;
using Livestream.Monitor.WPF.Core.UI;
using Livestream.Monitor.WPF.Model;
using INavigationService = Livestream.Monitor.WPF.Core.INavigationService;

namespace Livestream.Monitor.WPF.ViewModels
{
    public class TopStreamsViewModel : PagingConductor<TopStreamResult>
    {
        private const int STREAM_TILES_PER_PAGE = 15;
        private const int GameFilterDelayMilliseconds = 250;

        private readonly IMonitorStreamsModel monitorStreamsModel;
        private readonly ISettingsHandler settingsHandler;
        private readonly StreamLauncher streamLauncher;
        private readonly INavigationService navigationService;
        private readonly IApiClientFactory apiClientFactory;

        private bool hasNextPage;
        private int gameFilterGeneration;
        private int queryGeneration;

        #region Design time constructor

        public TopStreamsViewModel()
        {
            if (!Execute.InDesignMode)
                throw new InvalidOperationException("Constructor only accessible from design time");

            ItemsPerPage = STREAM_TILES_PER_PAGE;
            Items.Add(new TopStreamResult());
        }

        #endregion

        public TopStreamsViewModel(
            IMonitorStreamsModel monitorStreamsModel,
            ISettingsHandler settingsHandler,
            StreamLauncher streamLauncher,
            INavigationService navigationService,
            IApiClientFactory apiClientFactory)
        {
            this.monitorStreamsModel = monitorStreamsModel ?? throw new ArgumentNullException(nameof(monitorStreamsModel));
            this.settingsHandler = settingsHandler ?? throw new ArgumentNullException(nameof(settingsHandler));
            this.streamLauncher = streamLauncher ?? throw new ArgumentNullException(nameof(streamLauncher));
            this.navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
            this.apiClientFactory = apiClientFactory ?? throw new ArgumentNullException(nameof(apiClientFactory));

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

        public override bool CanNext => !LoadingItems && Items.Count == STREAM_TILES_PER_PAGE && hasNextPage;

        public bool CanRefreshItems => !LoadingItems;

        public bool LoadingItems
        {
            get;
            set
            {
                if (value == field) return;
                field = value;
                NotifyOfPropertyChange(() => LoadingItems);
                NotifyOfPropertyChange(() => CanPrevious);
                NotifyOfPropertyChange(() => CanNext);
                NotifyOfPropertyChange(() => CanRefreshItems);
            }
        }

        public string GameName
        {
            get;
            set
            {
                if (value == field) return;
                field = value;
                var generation = ++gameFilterGeneration;
                NotifyOfPropertyChange(() => GameName);
                if (IsActive) _ = ApplyGameFilterChange(generation);
            }
        }

        public BindableCollection<string> PossibleGameNames
        {
            get;
            set => Set(ref field, value);
        } = new();

        public bool ExpandPossibleGames
        {
            get;
            set => Set(ref field, value);
        }

        // Filled, not replaced: Caliburn.Micro 4 binds the view before initializing this screen.
        public BindableCollection<IApiClient> ApiClients { get; } = new();

        public IApiClient SelectedApiClient
        {
            get;
            set
            {
                if (Equals(value, field)) return;
                field = value;
                gameFilterGeneration++;
                NotifyOfPropertyChange(() => SelectedApiClient);
                MovePage();
                InitializeKnownGames();
            }
        }

        public async Task RefreshItems()
        {
            await EnsureItems();
        }

        public async Task OpenStream(TopStreamResult stream)
        {
            if (stream == null) return;

            await streamLauncher.OpenStream(stream.LivestreamModel, this);
        }

        public async Task OpenStreamInBrowser(TopStreamResult stream)
        {
            if (stream == null) return;

            var streamUrl = await stream.LivestreamModel.GetStreamUrl();
            Process.Start(new ProcessStartInfo(streamUrl) { UseShellExecute = true });
        }

        public async Task OpenChat(TopStreamResult stream)
        {
            if (stream?.LivestreamModel is not { ApiClient.HasChatSupport: true } livestream) return;

            await streamLauncher.OpenChat(livestream, this);
        }

        public void GotoVodViewer(TopStreamResult stream)
        {
            if (stream?.LivestreamModel is not { ApiClient.HasVodViewerSupport: true } livestream) return;

            navigationService.NavigateTo<VodListViewModel>(vm =>
            {
                vm.SelectedApiClient = livestream.ApiClient;
                vm.StreamDisplayName = livestream.ChannelIdentifier.DisplayName;
            });
        }

        public async Task CopyStreamUrl(TopStreamResult stream)
        {
            // clipboard.SetDataObject can sometimes fail due to a WPF bug, at least don't crash the app if this happens
            try
            {
                var streamUrl = await stream.LivestreamModel.GetStreamUrl();
                Clipboard.SetDataObject(streamUrl);
            }
            catch (Exception e)
            {
                await this.ShowMessageAsync("Error copying URL", $"An error occurred attempting to copy the URL: {e.ExtractErrorMessage()}");
            }
        }

        public void ToggleNotify(TopStreamResult stream)
        {
            if (stream == null) return;

            settingsHandler.Settings.ToggleNotify(stream.LivestreamModel);
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

        // HACK to avoid combobox auto selecting all text on opening, resulting in the user deleting what they just typed
        public void DropDownOpened(object sender)
        {
            var comboBox = sender as ComboBox;
            if (comboBox?.Text == null) return;
            TextBox textBox = comboBox.Template.FindName("PART_EditableTextBox", comboBox) as TextBox;
            if (textBox == null) return;
            textBox.SelectionStart = comboBox.Text.Length;
            textBox.SelectionLength = 0;
        }

        protected override async Task OnInitializeAsync(CancellationToken cancellationToken)
        {
            ApiClients.AddRange(apiClientFactory.GetAll().Where(x => x.HasTopStreamsSupport));

            if (SelectedApiClient == null)
                SelectedApiClient = apiClientFactory.Get<TwitchApiClient>();
            await base.OnInitializeAsync(cancellationToken);
        }

        protected override async void OnViewLoaded(object view)
        {
            if (Execute.InDesignMode) return;

            await LoadTopStreams();
            base.OnViewLoaded(view);
        }

        internal async Task LoadTopStreams()
        {
            hasNextPage = false;
            await EnsureItems();
            if (string.IsNullOrWhiteSpace(GameName) && PossibleGameNames.Count == 0) InitializeKnownGames();
        }

        private async void InitializeKnownGames()
        {
            var provider = SelectedApiClient;
            var generation = gameFilterGeneration;
            ReplaceGameSuggestions([]);

            if (provider?.HasTopStreamGameFilterSupport != true)
            {
                ExpandPossibleGames = false;
                return;
            }

            try
            {
                var games = await provider.GetKnownGameNames(null);
                if (generation != gameFilterGeneration) return;

                ReplaceGameSuggestions(games);
            }
            catch
            {
            }
        }

        private async Task UnmonitorStream(TopStreamResult topStreamResult)
        {
            try
            {
                var livestreamModel = monitorStreamsModel.Livestreams.FirstOrDefault(x => Equals(x, topStreamResult.LivestreamModel));

                if (livestreamModel != null)
                {
                    await monitorStreamsModel.RemoveLivestream(livestreamModel.ChannelIdentifier, new WpfDialogs(this));
                }
                topStreamResult.IsMonitored = false;
            }
            catch (Exception ex)
            {
                // A page left during the request no longer has a view to show the error in.
                if (IsActive) await this.ShowMessageAsync("Error Removing Livestream", ex.ExtractErrorMessage());
            }
        }

        private async Task MonitorStream(TopStreamResult topStreamResult)
        {
            try
            {
                await monitorStreamsModel.AddLivestream(topStreamResult.ChannelIdentifier, new WpfDialogs(this));
                topStreamResult.IsMonitored = true;
            }
            catch (Exception ex)
            {
                // A page left during the request no longer has a view to show the error in.
                if (IsActive) await this.ShowMessageAsync("Error Adding Livestream", ex.ExtractErrorMessage());
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

            var generation = ++queryGeneration;
            var filterGeneration = gameFilterGeneration;
            var provider = SelectedApiClient;

            LoadingItems = true;

            try
            {
                Items.Clear();

                if (!provider.IsAuthorized) await provider.Authorize(new WpfDialogs(this));
                if (!IsCurrent()) return;

                var topStreamsQuery = new TopStreamQuery
                {
                    GameName = provider.HasTopStreamGameFilterSupport ? GameName : null,
                    Skip = (Page - 1) * ItemsPerPage,
                    Take = ItemsPerPage,
                };
                var response = await provider.GetTopStreams(topStreamsQuery);
                if (!IsCurrent()) return;

                var monitoredStreams = monitorStreamsModel.Livestreams;

                var topStreamResults = new List<TopStreamResult>();
                foreach (var livestreamModel in response.LivestreamModels)
                {
                    var topStreamResult = new TopStreamResult(livestreamModel, livestreamModel.ChannelIdentifier);
                    topStreamResult.IsMonitored = monitoredStreams.Any(x => Equals(x, livestreamModel));
                    topStreamResult.LivestreamModel.SetLivestreamNotifyState(settingsHandler.Settings);

                    topStreamResults.Add(topStreamResult);
                }

                hasNextPage = response.HasNextPage;
                Items.AddRange(topStreamResults);
            }
            catch (Exception ex)
            {
                if (!IsCurrent()) return;

                var message =
                    $"Error getting top streams from '{PlatformNames.GetDisplayName(provider.ApiName)}'.{Environment.NewLine}{Environment.NewLine}{ex}";

                await this.ShowMessageAsync("Error", message);
            }
            finally
            {
                if (generation == queryGeneration) LoadingItems = false;
            }

            bool IsCurrent() => IsActive && generation == queryGeneration && filterGeneration == gameFilterGeneration;
        }

        private async Task ApplyGameFilterChange(int generation)
        {
            await Task.Delay(GameFilterDelayMilliseconds);
            if (!IsActive || generation != gameFilterGeneration) return;

            if (string.IsNullOrWhiteSpace(GameName) && PossibleGameNames.Count == 0) InitializeKnownGames();
            else if (!PossibleGameNames.Any(game => game.IsEqualTo(GameName))) UpdatePossibleGameNames();

            MovePage();
        }

        private async void UpdatePossibleGameNames()
        {
            var provider = SelectedApiClient;
            var game = GameName;
            var generation = gameFilterGeneration;
            if (provider?.HasTopStreamGameFilterSupport != true || string.IsNullOrWhiteSpace(game)) return;

            try
            {
                var games = await provider.GetKnownGameNames(game);
                if (generation != gameFilterGeneration) return;

                ReplaceGameSuggestions(games);
                ExpandPossibleGames = true;
            }
            catch
            {
                // make sure we dont crash just updating auto-completion options
            }
        }

        private void ReplaceGameSuggestions(IReadOnlyCollection<KnownGame> games)
        {
            // Removing the selected suggestion also clears the editable combo box's text.
            var enteredGame = GameName;
            for (var i = PossibleGameNames.Count - 1; i >= 0; i--)
                if (!PossibleGameNames[i].IsEqualTo(enteredGame)) PossibleGameNames.RemoveAt(i);

            if (games.Count == 0) return;

            var names = games.Select(game => game.GameName).Where(name => !PossibleGameNames.Contains(name));
            PossibleGameNames.AddRange(names);
        }
    }
}
