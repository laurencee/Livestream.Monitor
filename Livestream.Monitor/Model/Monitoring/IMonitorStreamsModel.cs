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

        bool CanOpenStream { get; }

        event EventHandler LivestreamsRefreshComplete;

        Task AddLivestream(ChannelIdentifier channelIdentifier, IViewAware viewAware);

        Task RemoveLivestream(ChannelIdentifier channelIdentifier);

        Task ImportFollows(string username, IApiClient apiClient);

        /// <summary> Refreshing data for all followed livestreams </summary>
        Task RefreshLivestreams();

        /// <summary> Prevent a specific error message from raising query failed exceptions </summary>
        /// <param name="errorToIgnore">The error message from a query failure to ignore</param>
        void IgnoreQueryFailure(string errorToIgnore);
    }
}