using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Caliburn.Micro;
using ExternalAPIs;
using Livestream.Monitor.Core;
using Livestream.Monitor.Core.Utility;
using Livestream.Monitor.Model;
using Livestream.Monitor.Model.ApiClients;
using Livestream.Monitor.Model.Monitoring;
using MahApps.Metro.Controls.Dialogs;
using static System.String;

namespace Livestream.Monitor.ViewModels
{
    public class HeaderViewModel : Screen
    {
        private const string TIP_ERROR_ADD_STREAM = "Tip: Make sure you have selected the right stream provider\nTip: Only input the streamers id and not the full url.";

        private readonly StreamLauncher streamLauncher;
        private readonly IApiClientFactory apiClientFactory;
        private readonly IWindowManager windowManager;
        private readonly ApiClientsQualitiesViewModel apiClientsQualitiesViewModel;
        private string streamName;
        private bool canRefreshLivestreams, canOpenChat, canAddStream;
        private IApiClient selectedApiClient;

        public HeaderViewModel()
        {
            if (!Execute.InDesignMode)
                throw new InvalidOperationException("Constructor only accessible from design time");
        }

        public HeaderViewModel(
            IMonitorStreamsModel monitorStreamsModel,
            StreamLauncher streamLauncher,
            FilterModel filterModelModel,
            IApiClientFactory apiClientFactory,
            ApiClientsQualitiesViewModel apiClientsQualitiesViewModel,
            IWindowManager windowManager)
        {
            FilterModel = filterModelModel ?? throw new ArgumentNullException(nameof(filterModelModel));
            MonitorStreamsModel = monitorStreamsModel ?? throw new ArgumentNullException(nameof(monitorStreamsModel));
            this.streamLauncher = streamLauncher ?? throw new ArgumentNullException(nameof(streamLauncher));
            this.apiClientFactory = apiClientFactory ?? throw new ArgumentNullException(nameof(apiClientFactory));
            this.windowManager = windowManager;
            this.apiClientsQualitiesViewModel = apiClientsQualitiesViewModel ?? throw new ArgumentNullException(nameof(apiClientsQualitiesViewModel));
        }

        public FilterModel FilterModel { get; }

        public bool CanAddStream
        {
            get { return canAddStream; }
            set => Set(ref canAddStream, value);
        }

        public string StreamName
        {
            get { return streamName; }
            set
            {
                if (value == streamName) return;
                streamName = value;
                NotifyOfPropertyChange(() => StreamName);
                CanAddStream = !IsNullOrWhiteSpace(streamName);
            }
        }

        public bool CanRefreshLivestreams
        {
            get { return canRefreshLivestreams; }
            set => Set(ref canRefreshLivestreams, value);
        }

        public bool CanOpenChat
        {
            get { return canOpenChat; }
            set => Set(ref canOpenChat, value);
        }

        public IMonitorStreamsModel MonitorStreamsModel { get; }

        public BindableCollection<IApiClient> ApiClients { get; set; } = new BindableCollection<IApiClient>();

        public IApiClient SelectedApiClient
        {
            get { return selectedApiClient; }
            set => Set(ref selectedApiClient, value);
        }

        public async Task AddStream()
        {
            if (IsNullOrWhiteSpace(StreamName) || !CanAddStream) return;

            CanAddStream = false;
            var dialogController = await this.ShowProgressAsync("Adding stream", $"Adding new stream '{StreamName}'");
            try
            {
                await MonitorStreamsModel.AddLivestream(new ChannelIdentifier(SelectedApiClient, StreamName), this);
                StreamName = null;
                await dialogController.CloseAsync();
            }
            catch (Exception ex)
            {
                CanAddStream = true; // on failure streamname not cleared so the user can try adding again
                await dialogController.CloseAsync();

                var httpException = ex.InnerException as HttpRequestWithStatusException;
                if (httpException != null && httpException.StatusCode == HttpStatusCode.NotFound)
                {
                    await this.ShowMessageAsync("Error adding stream.",
                        $"No channel found named '{StreamName}' for stream provider {SelectedApiClient.ApiName}{Environment.NewLine}" +
                        $"{Environment.NewLine}{TIP_ERROR_ADD_STREAM}");
                }
                else
                {
                    await this.ShowMessageAsync("Error adding stream", $"{ex.ExtractErrorMessage()}{Environment.NewLine}{TIP_ERROR_ADD_STREAM}");
                }
            }
        }

        public async Task ImportFollows()
        {
            if (!SelectedApiClient.HasFollowedChannelsQuerySupport)
            {
                await this.ShowMessageAsync("No import support",
                    $"{SelectedApiClient.ApiName} does not have support for importing followed streams");
                return;
            }

            var dialogSettings = new MetroDialogSettings() { AffirmativeButtonText = "Import" };
            var username = await this.ShowDialogAsync("Import Streams",
                $"Enter username for importing followed streams from '{SelectedApiClient.ApiName}'",
                dialogSettings);

            if (!IsNullOrWhiteSpace(username))
            {
                username = username.Trim();
                var dialogController = await this.ShowProgressAsync("Importing followed streams", $"Importing followed streams from '{SelectedApiClient.ApiName}' for username '{username}'");
                try
                {
                    await MonitorStreamsModel.ImportFollows(username, SelectedApiClient, this);
                }
                catch (Exception ex)
                {
                    await dialogController.CloseAsync();
                    await this.ShowMessageAsync("Error importing channels", ex.ExtractErrorMessage());
                }

                await dialogController.CloseAsync();
            }
        }

        public async Task RefreshLivestreams()
        {
            try
            {
                await MonitorStreamsModel.RefreshLivestreams();
            }
            catch (AggregateException aggregateException)
            {
                foreach (var ex in aggregateException.InnerExceptions)
                {
                    var messageDialogResult = await this.ShowMessageAsync(
                        "Error refreshing livestreams", ex.ExtractErrorMessage(),
                        MessageDialogStyle.AffirmativeAndNegative,
                        new MetroDialogSettings()
                        {
                            NegativeButtonText = "Ignore"
                        });

                    if (messageDialogResult == MessageDialogResult.Negative)
                        MonitorStreamsModel.IgnoreQueryFailure(ex.ExtractErrorMessage());
                }
            }
            catch (Exception ex)
            {
                await this.ShowMessageAsync("Error refreshing livestreams", ex.ExtractErrorMessage());
            }
        }

        public async Task OpenChat()
        {
            var selectedLivestream = MonitorStreamsModel.SelectedLivestream;
            if (selectedLivestream == null) return;

            await streamLauncher.OpenChat(selectedLivestream, this);
        }

        public void ToggleShowOnlineOnly()
        {
            FilterModel.ShowOnlineOnly = !FilterModel.ShowOnlineOnly;

            // remove focus from the toggle button after it has been clicked
            // looks nicer ¯\_(ツ)_/¯
            Keyboard.ClearFocus();
        }

        public async void KeyPressed(KeyEventArgs e)
        {
            if (e.Key == Key.Enter && CanAddStream)
                await AddStream();
        }

        public void OpenQualities()
        {
            var settings = new WindowSettingsBuilder()
                .AsTopmost()
                .SizeToContent(SizeToContent.Width)
                .Height(300)
                .Create();
            windowManager.ShowDialog(apiClientsQualitiesViewModel, null, settings);

            // remove focus from the toggle button after it has been clicked
            // looks nicer ¯\_(ツ)_/¯
            Keyboard.ClearFocus();
        }

        protected override void OnInitialize()
        {
            ApiClients.AddRange(apiClientFactory.GetAll());
            SelectedApiClient = apiClientFactory.Get<TwitchApiClient>();
            base.OnInitialize();
        }

        protected override void OnActivate()
        {
            SetFilterModelApiClients();
            MonitorStreamsModel.PropertyChanged += MonitorStreamsModelOnPropertyChanged;
            MonitorStreamsModel.Livestreams.CollectionChanged += LivestreamsOnCollectionChanged;
            base.OnActivate();
        }

        protected override void OnDeactivate(bool close)
        {
            MonitorStreamsModel.PropertyChanged -= MonitorStreamsModelOnPropertyChanged;
            MonitorStreamsModel.Livestreams.CollectionChanged -= LivestreamsOnCollectionChanged;
            base.OnDeactivate(close);
        }

        private void MonitorStreamsModelOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MonitorStreamsModel.CanRefreshLivestreams))
            {
                CanRefreshLivestreams = MonitorStreamsModel.CanRefreshLivestreams;
            }
            else if (e.PropertyName == nameof(MonitorStreamsModel.SelectedLivestream) || e.PropertyName == nameof(MonitorStreamsModel.CanOpenStream))
            {
                var selectedLivestream = MonitorStreamsModel.SelectedLivestream;
                CanOpenChat = selectedLivestream != null &&
                              selectedLivestream.ApiClient.HasChatSupport &&
                              !selectedLivestream.IsChatDisabled;
            }
        }

        private void LivestreamsOnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Reset ||
                e.Action == NotifyCollectionChangedAction.Replace ||
                e.NewItems != null ||
                e.OldItems != null)
            {
                SetFilterModelApiClients();
            }
        }

        private void SetFilterModelApiClients()
        {
            var apiClientNames = MonitorStreamsModel.Livestreams.Select(x => x.ApiClient.ApiName).Distinct().ToList();
            apiClientNames.Insert(0, FilterModel.AllApiClientsFilterName);
            FilterModel.ApiClientNames = new BindableCollection<string>(apiClientNames);

            // Set default selected stream provider to "all"
            if (FilterModel.SelectedApiClientName == null || !apiClientNames.Contains(FilterModel.SelectedApiClientName))
                FilterModel.SelectedApiClientName = FilterModel.AllApiClientsFilterName;
        }
    }
}