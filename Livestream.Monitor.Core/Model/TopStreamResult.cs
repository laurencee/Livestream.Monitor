using System;
using Livestream.Monitor.Core;
using Livestream.Monitor.Model.Monitoring;

namespace Livestream.Monitor.Model
{
    /// <summary> Composite class for searched top stream data </summary>
    public class TopStreamResult : ObservableModel
    {
        public TopStreamResult()
        {
            if (!ModelEnvironment.IsDesignMode) throw new InvalidOperationException("Design time only constructor");

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
            get;
            set => Set(ref field, value);
        }

        public bool IsBusy
        {
            get;
            set => Set(ref field, value);
        }

        public LivestreamModel LivestreamModel { get; }

        public ChannelIdentifier ChannelIdentifier { get; }
    }
}
