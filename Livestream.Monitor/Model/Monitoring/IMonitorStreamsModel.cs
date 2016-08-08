using System;
using System.Threading.Tasks;
using Caliburn.Micro;
using Livestream.Monitor.Model.ApiClients;

namespace Livestream.Monitor.Model.Monitoring
{
    public interface IMonitorStreamsModel : INotifyPropertyChangedEx
    {
        /// <summary> The list of followed livestreams, will be initialized on first access. </summary>
        BindableCollection<LivestreamModel> Livestreams { get; }

        LivestreamModel SelectedLivestream { get; set; }

        bool CanRefreshLivestreams { get; }

        DateTimeOffset LastRefreshTime { get; }

        string SelectedStreamQuality { get; set; }

        bool CanOpenStream { get; }

        event EventHandler LivestreamsRefreshComplete;

        Task AddLivestream(ChannelIdentifier channelIdentifier);

        Task RemoveLivestream(ChannelIdentifier channelIdentifier);

        Task ImportFollows(string username, IApiClient apiClient);

        /// <summary> Refreshing data for all followed livestreams </summary>
        Task RefreshLivestreams();
    }
}