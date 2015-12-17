using Caliburn.Micro;
using Livestream.Monitor.Model.Monitoring;

namespace Livestream.Monitor.Model
{
    /// <summary> Composite class for searched twitch stream data </summary>
    public class TwitchSearchStreamResult : PropertyChangedBase
    {
        private bool isMonitored;
        private bool isBusy;

        public bool IsMonitored
        {
            get { return isMonitored; }
            set
            {
                if (value == isMonitored) return;
                isMonitored = value;
                NotifyOfPropertyChange(() => IsMonitored);
            }
        }

        public bool IsBusy
        {
            get { return isBusy; }
            set
            {
                if (value == isBusy) return;
                isBusy = value;
                NotifyOfPropertyChange(() => IsBusy);
            }
        }

        public LivestreamModel LivestreamModel { get; set; } = new LivestreamModel();
    }
}