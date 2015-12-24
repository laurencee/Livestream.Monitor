using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Input;
using Caliburn.Micro;
using Livestream.Monitor.Core;
using Livestream.Monitor.Core.UI;
using Livestream.Monitor.Model;
using Livestream.Monitor.Model.Monitoring;
using MahApps.Metro.Controls.Dialogs;
using static System.String;

namespace Livestream.Monitor.ViewModels
{
    public class HeaderViewModel : Screen
    {
        private const string TIP_ERROR_ADD_STREAM = "Tip: Only input the streamers name and not the full twitch url.";

        private readonly IMonitorStreamsModel monitorStreamsModel;
        private readonly ISettingsHandler settingsHandler;
        private readonly StreamLauncher streamLauncher;
        private readonly TopTwitchStreamsViewModel topTwitchStreamsViewModel;
        private readonly IEventAggregator eventAggregator;
        private string streamName;
        private bool canRefreshLivestreams;
        private StreamQuality? selectedStreamQuality;
        private bool canOpenStream;
        private bool canOpenChat;
        private bool canAddStream;

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
            TopTwitchStreamsViewModel topTwitchStreamsViewModel,
            IEventAggregator eventAggregator)
        {
            if (monitorStreamsModel == null) throw new ArgumentNullException(nameof(monitorStreamsModel));
            if (settingsHandler == null) throw new ArgumentNullException(nameof(settingsHandler));
            if (streamLauncher == null) throw new ArgumentNullException(nameof(streamLauncher));
            if (filterModelModel == null) throw new ArgumentNullException(nameof(filterModelModel));
            if (topTwitchStreamsViewModel == null) throw new ArgumentNullException(nameof(topTwitchStreamsViewModel));
            if (eventAggregator == null) throw new ArgumentNullException(nameof(eventAggregator));

            FilterModel = filterModelModel;
            this.monitorStreamsModel = monitorStreamsModel;
            this.settingsHandler = settingsHandler;
            this.streamLauncher = streamLauncher;
            this.topTwitchStreamsViewModel = topTwitchStreamsViewModel;
            this.eventAggregator = eventAggregator;
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

        public async Task AddStream()
        {
            if (IsNullOrWhiteSpace(StreamName) || !CanAddStream) return;

            CanAddStream = false;
            var dialogController = await this.ShowProgressAsync("Adding stream", $"Adding new stream '{StreamName}'");
            try
            {
                await monitorStreamsModel.AddLivestream(new LivestreamModel() { Id = StreamName });
                StreamName = null;
                await dialogController.CloseAsync();
            }
            catch (HttpRequestException httpException)
                when (httpException.Message == "Response status code does not indicate success: 404 (Not Found).")
            {
                CanAddStream = true;
                await dialogController.CloseAsync();
                await this.ShowMessageAsync("Error adding stream.", $"No channel found named '{StreamName}'{Environment.NewLine}" +
                                                                    $"{Environment.NewLine}{TIP_ERROR_ADD_STREAM}");
            }
            catch (Exception ex)
            {
                CanAddStream = true; // on failure streamname not cleared so the user can try adding again
                await dialogController.CloseAsync();
                await this.ShowMessageAsync("Error adding stream.", $"{TIP_ERROR_ADD_STREAM}{Environment.NewLine}{ex.Message}");
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
            streamLauncher.StartStream(monitorStreamsModel.SelectedLivestream);
        }

        public async Task OpenChat()
        {
            var chromeLocation = settingsHandler.Settings.ChromeFullPath;
            if (!File.Exists(chromeLocation))
            {
                await this.ShowMessageAsync("Chrome not found",
                    $"Could not find chrome @ {chromeLocation}.{Environment.NewLine} The chat function relies on chrome to function.");
                return;
            }

            var selectedLivestream = monitorStreamsModel.SelectedLivestream;
            if (selectedLivestream == null) return;

            string chromeArgs = $"--app=http://www.twitch.tv/{selectedLivestream.DisplayName}/chat?popout=true --window-size=350,758";

            await Task.Run(async () =>
            {
                try
                {
                    var proc = new Process()
                    {
                        StartInfo =
                        {
                            FileName = chromeLocation,
                            Arguments = chromeArgs,
                            CreateNoWindow = true,
                            UseShellExecute = false
                        }
                    };

                    proc.Start();
                }
                catch (Exception ex)
                {
                    await this.ShowMessageAsync("Error launching chat", ex.Message);
                }
            });
        }

        public async void KeyPressed(KeyEventArgs e)
        {
            if (e.Key == Key.Enter && CanAddStream)
                await AddStream();
        }

        public void ShowTopTwitchStreams()
        {
            eventAggregator.PublishOnUIThread(new ActivateScreen(topTwitchStreamsViewModel));
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
                CanOpenChat = selectedLivestream != null;
                StreamQualities.Clear();
                selectedStreamQuality = null;
                if (CanOpenStream)
                {
                    if (selectedLivestream.IsPartner)
                    {
                        StreamQualities.AddRange(Enum.GetValues(typeof(StreamQuality)).Cast<StreamQuality>());
                        // set field instead of property so we dont update user settings
                        selectedStreamQuality = settingsHandler.Settings.DefaultStreamQuality;
                    }
                    else
                    {
                        StreamQualities.Add(StreamQuality.Source); //only source mode is available for non-twitch partners
                        // set field instead of property so we dont update user settings
                        selectedStreamQuality = StreamQuality.Source;
                    }
                }
                NotifyOfPropertyChange(() => SelectedStreamQuality);
            }
        }
    }
}