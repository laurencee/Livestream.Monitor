using System;
using Caliburn.Micro;

namespace Livestream.Monitor.Model
{
    /// <summary> Composite class for searched top stream data </summary>
    public class TopStreamResult : PropertyChangedBase
    {
        private bool isMonitored;
        private bool isBusy;

        public TopStreamResult(LivestreamModel livestreamModel)
        {
            if (livestreamModel == null) throw new ArgumentNullException(nameof(livestreamModel));
            LivestreamModel = livestreamModel;
        }

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

        public LivestreamModel LivestreamModel { get; }
    }
}