using System;
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
        private readonly IWindowManager windowManager;
        private string streamName;
        private bool canShowImportWindow = true;

        public HeaderViewModel()
        {
            if (!Execute.InDesignMode)
                throw new InvalidOperationException("Constructor only accessible from design time");
        }

        public HeaderViewModel(
            IMonitorStreamsModel monitorStreamsModel,
            IWindowManager windowManager)
        {
            if (monitorStreamsModel == null) throw new ArgumentNullException(nameof(monitorStreamsModel));
            if (windowManager == null) throw new ArgumentNullException(nameof(windowManager));

            this.monitorStreamsModel = monitorStreamsModel;
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
    }
}