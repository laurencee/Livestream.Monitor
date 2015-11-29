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
using Livestream.Monitor.Model;
using MahApps.Metro.Controls.Dialogs;
using static System.String;

namespace Livestream.Monitor.ViewModels
{
    public class HeaderViewModel : Screen
    {
        private readonly IMonitorStreamsModel monitorStreamsModel;
        private readonly ISettingsHandler settingsHandler;
        private readonly StreamLauncher streamLauncher;
        private string streamName;
        private bool canRefreshChannels;
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
            FilterModel filterModelModel)
        {
            if (monitorStreamsModel == null) throw new ArgumentNullException(nameof(monitorStreamsModel));
            if (settingsHandler == null) throw new ArgumentNullException(nameof(settingsHandler));
            if (streamLauncher == null) throw new ArgumentNullException(nameof(streamLauncher));
            if (filterModelModel == null) throw new ArgumentNullException(nameof(filterModelModel));
            
            FilterModel = filterModelModel;
            this.monitorStreamsModel = monitorStreamsModel;
            this.settingsHandler = settingsHandler;
            this.streamLauncher = streamLauncher;
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

        public bool CanRefreshChannels
        {
            get { return canRefreshChannels; }
            set
            {
                if (value == canRefreshChannels) return;
                canRefreshChannels = value;
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
                await monitorStreamsModel.AddStream(new ChannelData() { ChannelName = StreamName });
                StreamName = null;
            }
            catch (HttpRequestException httpException)
                when (httpException.Message == "Response status code does not indicate success: 404 (Not Found).")
            {
                CanAddStream = true;
                await this.ShowMessageAsync("Error adding stream", $"No channel found named '{StreamName}'");
            }
            catch (Exception ex)
            {
                CanAddStream = true; // on failure streamname not cleared so the user can try adding again
                await this.ShowMessageAsync("Error adding stream", ex.Message);
            }

            await dialogController.CloseAsync();
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
                    await this.ShowMessageAsync("Error importing channels", ex.Message);
                    // TODO log import error
                }

                await dialogController.CloseAsync();
            }
        }

        public async Task RefreshChannels()
        {
            await monitorStreamsModel.RefreshChannels();
        }

        public void OpenStream()
        {
            streamLauncher.StartStream();
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

            var selectedChannel = monitorStreamsModel.SelectedChannel;
            if (selectedChannel == null) return;

            string chromeArgs = $"--app=http://www.twitch.tv/{selectedChannel.ChannelName}/chat?popout=true --window-size=350,758";

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

        protected override void OnActivate()
        {
            monitorStreamsModel.PropertyChanged += MonitorStreamsModelOnPropertyChanged;
            base.OnActivate();
        }

        private void MonitorStreamsModelOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(monitorStreamsModel.CanRefreshChannels))
            {
                CanRefreshChannels = monitorStreamsModel.CanRefreshChannels;
            }
            else if (e.PropertyName == nameof(monitorStreamsModel.SelectedChannel))
            {
                var selectedChannel = monitorStreamsModel.SelectedChannel;
                CanOpenStream = selectedChannel != null && selectedChannel.Live;
                CanOpenChat = selectedChannel != null;
                StreamQualities.Clear();
                selectedStreamQuality = null;
                if (CanOpenStream)
                {
                    if (selectedChannel.IsPartner)
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