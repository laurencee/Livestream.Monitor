using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Caliburn.Micro;
using Livestream.Monitor.Core;
using Livestream.Monitor.Model;
using static System.String;

namespace Livestream.Monitor.ViewModels
{
    public class HeaderViewModel : Screen
    {
        private const string ChromeLocation = @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe";

        private readonly IMonitorStreamsModel monitorStreamsModel;
        private readonly ISettingsHandler settingsHandler;
        private readonly IWindowManager windowManager;
        private readonly StreamLauncher streamLauncher;
        private string streamName;
        private bool canShowImportWindow = true;
        private bool canRefreshChannels;
        private StreamQuality? selectedStreamQuality;
        private bool canOpenStream;
        private bool canOpenChat;

        public HeaderViewModel()
        {
            if (!Execute.InDesignMode)
                throw new InvalidOperationException("Constructor only accessible from design time");
        }

        public HeaderViewModel(
            IMonitorStreamsModel monitorStreamsModel,
            ISettingsHandler settingsHandler,
            IWindowManager windowManager,
            StreamLauncher streamLauncher,
            FilterModel filterModelModel)
        {
            if (monitorStreamsModel == null) throw new ArgumentNullException(nameof(monitorStreamsModel));
            if (settingsHandler == null) throw new ArgumentNullException(nameof(settingsHandler));
            if (windowManager == null) throw new ArgumentNullException(nameof(windowManager));
            if (streamLauncher == null) throw new ArgumentNullException(nameof(streamLauncher));
            if (filterModelModel == null) throw new ArgumentNullException(nameof(filterModelModel));
            
            FilterModel = filterModelModel;
            this.monitorStreamsModel = monitorStreamsModel;
            this.settingsHandler = settingsHandler;
            this.windowManager = windowManager;
            this.streamLauncher = streamLauncher;
        }

        public FilterModel FilterModel { get; }

        public bool CanAddStream => !IsNullOrWhiteSpace(StreamName);

        public string StreamName
        {
            get { return streamName; }
            set
            {
                if (value == streamName) return;
                streamName = value;
                NotifyOfPropertyChange(() => StreamName);
                NotifyOfPropertyChange(() => CanAddStream);
            }
        }

        public bool CanShowImportWindow
        {
            get { return canShowImportWindow; }
            set
            {
                if (value == canShowImportWindow) return;
                canShowImportWindow = value;
                NotifyOfPropertyChange();
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
            if (IsNullOrWhiteSpace(StreamName)) return;

            await monitorStreamsModel.AddStream(new ChannelData() { ChannelName = StreamName });
        }

        public void ShowImportWindow()
        {
            CanShowImportWindow = false;
            var importChannelsViewModel = new ImportChannelsViewModel(monitorStreamsModel);
            importChannelsViewModel.Deactivated += (sender, args) => CanShowImportWindow = true;

            var settings = new WindowSettingsBuilder().SizeToContent().Create();
            windowManager.ShowWindow(importChannelsViewModel, null, settings);
        }

        public async Task RefreshChannels()
        {
            await monitorStreamsModel.RefreshChannels();
        }

        public void OpenStream()
        {
            streamLauncher.StartStream();
        }

        public void OpenChat()
        {
            if (!File.Exists(ChromeLocation))
            {
                var msgBox = new MessageBoxViewModel()
                {
                    DisplayName = "Chrome not found",
                    MessageText = $"Could not find chrome @ {ChromeLocation}.{Environment.NewLine} The chat function relies on chrome to function."
                };
                var settings = new WindowSettingsBuilder().SizeToContent()
                                                      .WithWindowStyle(WindowStyle.ToolWindow)
                                                      .WithResizeMode(ResizeMode.NoResize)
                                                      .Create();
                windowManager.ShowWindow(msgBox, null, settings);
                return;
            }

            var selectedChannel = monitorStreamsModel.SelectedChannel;
            if (selectedChannel == null) return;

            string chromeArgs = $"--app=http://www.twitch.tv/{selectedChannel.ChannelName}/chat?popout=true --window-size=350,758";

            Task.Run(() =>
            {
                try
                {
                    var proc = new Process()
                    {
                        StartInfo =
                        {
                            FileName = ChromeLocation,
                            Arguments = chromeArgs,
                            CreateNoWindow = true,
                            UseShellExecute = false
                        }
                    };

                    proc.Start();
                }
                catch (Exception)
                {
                    // TODO - log errors opening chat
                }
            });
        }

        public async void KeyPressed(KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
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