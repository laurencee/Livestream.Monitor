using System;
using System.Threading.Tasks;
using Caliburn.Micro;
using Livestream.Monitor.Model;
using static System.String;

namespace Livestream.Monitor.ViewModels
{
    public class HeaderViewModel : Screen
    {
        private readonly IMonitorStreamsModel monitorStreamsModel;
        private string streamName;

        public HeaderViewModel()
        {
            if (!Execute.InDesignMode)
                throw new InvalidOperationException("Constructor only accessible from design time");
        }

        public HeaderViewModel(
            IMonitorStreamsModel monitorStreamsModel)
        {
            if (monitorStreamsModel == null) throw new ArgumentNullException(nameof(monitorStreamsModel));
            this.monitorStreamsModel = monitorStreamsModel;
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

        public async Task AddStream()
        {
            if (IsNullOrWhiteSpace(StreamName)) return;
            
            await monitorStreamsModel.AddStream(new ChannelData() { ChannelName = StreamName});
        }
        
        public async Task ImportFollowList(string username)
        {
            if (IsNullOrWhiteSpace(username)) return;
            
            await monitorStreamsModel.ImportFollows(username);
        }
    }
}