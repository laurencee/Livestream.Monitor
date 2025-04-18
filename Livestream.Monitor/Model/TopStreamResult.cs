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

        public TopStreamResult()
        {
            if (!Execute.InDesignMode) throw new InvalidOperationException("Design time only constructor");

            LivestreamModel = new LivestreamModel();
            ChannelIdentifier = new ChannelIdentifier();
        }

        public TopStreamResult(LivestreamModel livestreamModel, ChannelIdentifier channelIdentifier)
        {
            LivestreamModel = livestreamModel ?? throw new ArgumentNullException(nameof(livestreamModel));
            ChannelIdentifier = channelIdentifier ?? throw new ArgumentNullException(nameof(channelIdentifier));
        }

        public bool IsMonitored
        {
            get { return isMonitored; }
            set => Set(ref isMonitored, value);
        }

        public bool IsBusy
        {
            get { return isBusy; }
            set => Set(ref isBusy, value);
        }

        public LivestreamModel LivestreamModel { get; }

        public ChannelIdentifier ChannelIdentifier { get; }
    }
}