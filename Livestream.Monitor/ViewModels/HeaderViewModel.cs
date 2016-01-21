using System;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Input;
using Caliburn.Micro;
using HttpCommon;
using Livestream.Monitor.Core;
using Livestream.Monitor.Model;
using Livestream.Monitor.Model.Monitoring;
using Livestream.Monitor.Model.StreamProviders;
using MahApps.Metro.Controls.Dialogs;
using static System.String;

namespace Livestream.Monitor.ViewModels
{
    public class HeaderViewModel : Screen
    {
        private const string TIP_ERROR_ADD_STREAM = "Tip: Make sure you have selected the right stream provider\nTip: Only input the streamers id and not the full url.";

        private readonly IMonitorStreamsModel monitorStreamsModel;
        private readonly ISettingsHandler settingsHandler;
        private readonly StreamLauncher streamLauncher;
        private readonly IStreamProviderFactory streamProviderFactory;
        private string streamName;
        private bool canRefreshLivestreams;
        private StreamQuality? selectedStreamQuality;
        private bool canOpenStream;
        private bool canOpenChat;
        private bool canAddStream;
        private IStreamProvider selectedStreamProvider;

        public HeaderViewModel()
        {
            if (!Execute.InDesignMode)
                throw new InvalidOperationException("Constructor only accessible from design time");
        }

        public HeaderViewModel(
            IMonitorStreamsModel monitorStreamsModel,
            ISettingsHandler settingsHandler,
            StreamLauncher streamLauncher,
            FilterModel filterModelModel,
            IStreamProviderFactory streamProviderFactory)
        {
            if (monitorStreamsModel == null) throw new ArgumentNullException(nameof(monitorStreamsModel));
            if (settingsHandler == null) throw new ArgumentNullException(nameof(settingsHandler));
            if (streamLauncher == null) throw new ArgumentNullException(nameof(streamLauncher));
            if (filterModelModel == null) throw new ArgumentNullException(nameof(filterModelModel));
            if (streamProviderFactory == null) throw new ArgumentNullException(nameof(streamProviderFactory));

            FilterModel = filterModelModel;
            this.monitorStreamsModel = monitorStreamsModel;
            this.settingsHandler = settingsHandler;
            this.streamLauncher = streamLauncher;
            this.streamProviderFactory = streamProviderFactory;
        }

        public FilterModel FilterModel { get; }

        public bool CanAddStream
        {
            get { return canAddStream; }
            set
            {
                if (value == canAddStream) return;
                canAddStream = value;
                NotifyOfPropertyChange();
            }
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
            set
            {
                if (value == canRefreshLivestreams) return;
                canRefreshLivestreams = value;
                NotifyOfPropertyChange();
            }
        }

        public bool CanOpenStream
        {
            get { return canOpenStream; }
            set
            {
                if (value == canOpenStream) return;
                canOpenStream = value;
                NotifyOfPropertyChange();
            }
        }

        public bool CanOpenChat
        {
            get { return canOpenChat; }
            set
            {
                if (value == canOpenChat) return;
                canOpenChat = value;
                NotifyOfPropertyChange();
            }
        }

        public StreamQuality? SelectedStreamQuality
        {
            get { return selectedStreamQuality; }
            set
            {
                if (value == selectedStreamQuality) return;
                selectedStreamQuality = value;
                NotifyOfPropertyChange();
                if (selectedStreamQuality.HasValue)
                    settingsHandler.Settings.DefaultStreamQuality = selectedStreamQuality.Value;
            }
        }

        public BindableCollection<StreamQuality> StreamQualities { get; set; } = new BindableCollection<StreamQuality>();

        public BindableCollection<IStreamProvider> StreamProviders { get; set; } = new BindableCollection<IStreamProvider>();

        public IStreamProvider SelectedStreamProvider
        {
            get { return selectedStreamProvider; }
            set
            {
                if (Equals(value, selectedStreamProvider)) return;
                selectedStreamProvider = value;
                NotifyOfPropertyChange(() => SelectedStreamProvider);
            }
        }
        
        public async Task AddStream()
        {
            if (IsNullOrWhiteSpace(StreamName) || !CanAddStream) return;

            CanAddStream = false;
            var dialogController = await this.ShowProgressAsync("Adding stream", $"Adding new stream '{StreamName}'");
            try
            {
                await monitorStreamsModel.AddLivestream(new LivestreamModel()
                {
                    Id = StreamName,
                    StreamProvider = SelectedStreamProvider
                });
                StreamName = null;
                await dialogController.CloseAsync();
            }
            catch (HttpRequestWithStatusException httpException) when (httpException.StatusCode == HttpStatusCode.NotFound)
            {
                CanAddStream = true;
                await dialogController.CloseAsync();
                await this.ShowMessageAsync("Error adding stream.", $"No channel found named '{StreamName}' for stream provider {SelectedStreamProvider.ProviderName}{Environment.NewLine}" +
                                                                    $"{Environment.NewLine}{TIP_ERROR_ADD_STREAM}");
            }
            catch (Exception ex)
            {
                CanAddStream = true; // on failure streamname not cleared so the user can try adding again
                await dialogController.CloseAsync();
                await this.ShowMessageAsync("Error adding stream.", $"{ex.Message}{Environment.NewLine}{TIP_ERROR_ADD_STREAM}");
            }
        }

        public async Task ImportFollows()
        {
            var dialogSettings = new MetroDialogSettings() { AffirmativeButtonText = "Import" };
            var username = await this.ShowDialogAsync("Import Streams", "Enter username for importing followed streams", dialogSettings);

            if (!IsNullOrWhiteSpace(username))
            {
                username = username.Trim();
                var dialogController = await this.ShowProgressAsync("Importing followed streams", $"Importing followed streams from user '{username}'");
                try
                {
                    await monitorStreamsModel.ImportFollows(username);
                }
                catch (Exception ex)
                {
                    await dialogController.CloseAsync();
                    await this.ShowMessageAsync("Error importing channels", ex.Message);
                    // TODO log import error
                }

                await dialogController.CloseAsync();
            }
        }

        public async Task RefreshLivestreams()
        {
            await monitorStreamsModel.RefreshLivestreams();
        }

        public void OpenStream()
        {
            streamLauncher.OpenStream(monitorStreamsModel.SelectedLivestream);
        }

        public async Task OpenChat()
        {
            var selectedLivestream = monitorStreamsModel.SelectedLivestream;
            if (selectedLivestream == null) return;

            await streamLauncher.OpenChat(selectedLivestream, this);
        }

        public async void KeyPressed(KeyEventArgs e)
        {
            if (e.Key == Key.Enter && CanAddStream)
                await AddStream();
        }

        protected override void OnInitialize()
        {
            StreamProviders.AddRange(streamProviderFactory.GetAll());
            SelectedStreamProvider = streamProviderFactory.Get<TwitchStreamProvider>();
            base.OnInitialize();
        }

        protected override void OnActivate()
        {
            monitorStreamsModel.PropertyChanged += MonitorStreamsModelOnPropertyChanged;
            base.OnActivate();
        }

        protected override void OnDeactivate(bool close)
        {
            monitorStreamsModel.PropertyChanged -= MonitorStreamsModelOnPropertyChanged;
            base.OnDeactivate(close);
        }

        private void MonitorStreamsModelOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(monitorStreamsModel.CanRefreshLivestreams))
            {
                CanRefreshLivestreams = monitorStreamsModel.CanRefreshLivestreams;
            }
            else if (e.PropertyName == nameof(monitorStreamsModel.SelectedLivestream))
            {
                var selectedLivestream = monitorStreamsModel.SelectedLivestream;
                CanOpenStream = selectedLivestream != null && selectedLivestream.Live;
                CanOpenChat = selectedLivestream != null && selectedLivestream.StreamProvider.HasChatSupport;
                StreamQualities.Clear();
                selectedStreamQuality = null;
                if (CanOpenStream)
                {
                    if (selectedLivestream.IsPartner) // twitch partner specific
                    {
                        StreamQualities.AddRange(Enum.GetValues(typeof(StreamQuality)).Cast<StreamQuality>());
                        // set field instead of property so we dont update user settings
                        selectedStreamQuality = settingsHandler.Settings.DefaultStreamQuality;
                    }
                    else
                    {
                        StreamQualities.AddRange(new[] { StreamQuality.Best, StreamQuality.Worst, });
                        // set field instead of property so we dont update user settings
                        selectedStreamQuality = StreamQuality.Best;
                    }
                }
                NotifyOfPropertyChange(() => SelectedStreamQuality);
            }
        }
    }
}