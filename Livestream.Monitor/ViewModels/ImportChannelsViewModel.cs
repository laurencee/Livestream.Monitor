using System;
using System.Threading.Tasks;
using Caliburn.Micro;
using Livestream.Monitor.Core;
using Livestream.Monitor.Model;

namespace Livestream.Monitor.ViewModels
{
    public class ImportChannelsViewModel : Screen
    {
        private readonly IMonitorStreamsModel monitorStreamsModel;
        private string username;
        private bool canImport = true;

        public ImportChannelsViewModel(IMonitorStreamsModel monitorStreamsModel)
        {
            if (monitorStreamsModel == null) throw new ArgumentNullException(nameof(monitorStreamsModel));
            this.monitorStreamsModel = monitorStreamsModel;
        }

        public override string DisplayName { get; set; } = "Import Channels";

        public bool CanImport
        {
            get { return canImport && !string.IsNullOrWhiteSpace(Username); }
            set
            {
                if (value == canImport) return;
                canImport = value;
                NotifyOfPropertyChange(() => CanImport);
            }
        }

        public string Username
        {
            get { return username; }
            set
            {
                if (value == username) return;
                username = value;
                NotifyOfPropertyChange();
                NotifyOfPropertyChange(() => CanImport);
            }
        }

        public async Task Import()
        {
            CanImport = false;
            try
            {
                await monitorStreamsModel.ImportFollows(Username);
                TryClose();
            }
            catch (Exception)
            {
                // do something with exceptions from imports
            }
            CanImport = true;
        }

        protected override void OnViewLoaded(object view)
        {
            base.OnViewLoaded(view);
            this.SetFocus(() => Username);
        }
    }
}
