using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Caliburn.Micro;
using Livestream.Monitor.Core;
using Livestream.Monitor.Model;
using static System.String;

namespace Livestream.Monitor.ViewModels
{
    public class HeaderViewModel : Screen
    {
        private readonly IMonitorStreamsModel monitorStreamsModel;
        private readonly ISettingsHandler settingsHandler;
        private readonly IWindowManager windowManager;
        private string streamName;
        private bool canShowImportWindow = true;
        private bool canRefreshChannels;
        private string streamQuality;

        public HeaderViewModel()
        {
            if (!Execute.InDesignMode)
                throw new InvalidOperationException("Constructor only accessible from design time");
        }

        public HeaderViewModel(
            IMonitorStreamsModel monitorStreamsModel,
            ISettingsHandler settingsHandler,
            IWindowManager windowManager)
        {
            if (monitorStreamsModel == null) throw new ArgumentNullException(nameof(monitorStreamsModel));
            if (settingsHandler == null) throw new ArgumentNullException(nameof(settingsHandler));
            if (windowManager == null) throw new ArgumentNullException(nameof(windowManager));
            
            this.monitorStreamsModel = monitorStreamsModel;
            this.settingsHandler = settingsHandler;
            this.windowManager = windowManager;
        }

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

        public string SelectedStreamQuality
        {
            get { return streamQuality; }
            set
            {
                if (value == streamQuality) return;
                streamQuality = value;
                NotifyOfPropertyChange();
                settingsHandler.Settings.DefaultStreamQuality = (StreamQuality) Enum.Parse(typeof (StreamQuality), streamQuality);
            }
        }

        public BindableCollection<string> StreamQualities { get; set; } = new BindableCollection<string>();

        public async Task AddStream()
        {
            if (IsNullOrWhiteSpace(StreamName)) return;
            
            await monitorStreamsModel.AddStream(new ChannelData() { ChannelName = StreamName});
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

        protected override void OnActivate()
        {
            monitorStreamsModel.PropertyChanged += MonitorStreamsModelOnPropertyChanged;
            SelectedStreamQuality = settingsHandler.Settings.DefaultStreamQuality.ToString();
            StreamQualities.AddRange(Enum.GetNames(typeof(StreamQuality)));
            base.OnActivate();
        }

        private void MonitorStreamsModelOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(monitorStreamsModel.CanRefreshChannels))
                CanRefreshChannels = monitorStreamsModel.CanRefreshChannels;
        }
    }
}