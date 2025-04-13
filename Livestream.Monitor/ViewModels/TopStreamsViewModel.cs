﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Caliburn.Micro;
using ExternalAPIs;
using Livestream.Monitor.Core;
using Livestream.Monitor.Core.UI;
using Livestream.Monitor.Model;
using Livestream.Monitor.Model.ApiClients;
using Livestream.Monitor.Model.Monitoring;
using INavigationService = Livestream.Monitor.Core.INavigationService;

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
        private bool hasNextPage;
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
            get => loadingItems;
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
            get => gameName;
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
            get => possibleGameNames;
            set
            {
                if (Equals(value, possibleGameNames)) return;
                possibleGameNames = value;
                NotifyOfPropertyChange(() => PossibleGameNames);
            }
        }

        public bool ExpandPossibleGames
        {
            get => expandPossibleGames;
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
            get => selectedApiClient;
            set
            {
                if (Equals(value, selectedApiClient)) return;
                selectedApiClient = value;
                NotifyOfPropertyChange(() => SelectedApiClient);
                MovePage();
                InitializeKnownGames();
            }
        }

        // ReSharper disable UnusedMember.Global - these are bound automatically via yaml

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

            var streamUrl = await stream.LivestreamModel.GetStreamUrl;
            Process.Start(streamUrl);
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
                vm.SelectedApiClient = stream.LivestreamModel.ApiClient;
                vm.StreamDisplayName = stream.LivestreamModel.ChannelIdentifier.DisplayName;
            });
        }

        public async Task CopyStreamUrl(TopStreamResult stream)
        {
            // clipboard.SetDataObject can sometimes fail due to a WPF bug, at least don't crash the app if this happens
            try
            {
                var streamUrl = await stream.LivestreamModel.GetStreamUrl;
                Clipboard.SetDataObject(streamUrl);
            }
            catch (Exception e)
            {
                await this.ShowMessageAsync("Error copying url",
                    $"An error occurred attempting to copy the url, please try again: {e.ExtractErrorMessage()}");
            }
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

        // ReSharper restore UnusedMember.Global

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

            hasNextPage = false;
            await EnsureItems();
            if (!PossibleGameNames.Any()) InitializeKnownGames();

            base.OnViewLoaded(view);
        }

        private async void InitializeKnownGames()
        {
            PossibleGameNames.Clear();
            if (!SelectedApiClient.HasTopStreamGameFilterSupport) return;

            try
            {
                var games = await SelectedApiClient.GetKnownGameNames(null);
                PossibleGameNames.AddRange(games.Select(x => x.GameName));
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
                    await monitorStreamsModel.RemoveLivestream(livestreamModel.ChannelIdentifier, this);
                }
                topStreamResult.IsMonitored = false;
            }
            catch (Exception ex)
            {
                await this.ShowMessageAsync("Error Removing Livestream", ex.ExtractErrorMessage());
            }
        }

        private async Task MonitorStream(TopStreamResult topStreamResult)
        {
            try
            {
                await monitorStreamsModel.AddLivestream(topStreamResult.ChannelIdentifier, this);
                topStreamResult.IsMonitored = true;
            }
            catch (Exception ex)
            {
                await this.ShowMessageAsync("Error Adding Livestream", ex.ExtractErrorMessage());
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

                if (!SelectedApiClient.IsAuthorized) await SelectedApiClient.Authorize(this);

                var topStreamsQuery = new TopStreamQuery
                {
                    GameName = GameName,
                    Skip = (Page - 1) * ItemsPerPage,
                    Take = ItemsPerPage,
                };
                var response = await SelectedApiClient.GetTopStreams(topStreamsQuery);
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
                await this.ShowMessageAsync("Error",
                    $"Error getting top streams from '{SelectedApiClient.ApiName}'.{Environment.NewLine}{Environment.NewLine}{ex}");
            }

            LoadingItems = false;
        }

        private async void UpdatePossibleGameNames()
        {
            var game = GameName; // store local variable in case GameName changes while this is running
            if (!SelectedApiClient.HasTopStreamGameFilterSupport || string.IsNullOrWhiteSpace(game)) return;

            try
            {
                var games = await SelectedApiClient.GetKnownGameNames(game);
                var oldItems = PossibleGameNames.ToList();
                PossibleGameNames.AddRange(games.Select(x => x.GameName));
                PossibleGameNames.RemoveRange(oldItems);
                ExpandPossibleGames = true;
            }
            catch
            {
                // make sure we dont crash just updating auto-completion options
            }
        }
    }
}