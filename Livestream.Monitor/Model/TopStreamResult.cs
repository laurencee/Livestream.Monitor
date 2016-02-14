using System;
using Caliburn.Micro;
using Livestream.Monitor.Model.Monitoring;

namespace Livestream.Monitor.Model
{
    /// <summary> Composite class for searched top stream data </summary>
    public class TopStreamResult : PropertyChangedBase
    {
        private bool isMonitored;
        private bool isBusy;

        public TopStreamResult(LivestreamModel livestreamModel, ChannelIdentifier channelIdentifier)
        {
            if (livestreamModel == null) throw new ArgumentNullException(nameof(livestreamModel));
            if (channelIdentifier == null) throw new ArgumentNullException(nameof(channelIdentifier));
            LivestreamModel = livestreamModel;
            ChannelIdentifier = channelIdentifier;
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

        public ChannelIdentifier ChannelIdentifier { get; }
    }
}