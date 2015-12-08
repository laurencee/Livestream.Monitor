using System;
using System.Threading.Tasks;
using Caliburn.Micro;

namespace Livestream.Monitor.Model.Monitoring
{
    public interface IMonitorStreamsModel : INotifyPropertyChangedEx
    {
        /// <summary> The list of followed livestreams, will be initialized on first access. </summary>
        BindableCollection<LivestreamModel> Livestreams { get; }

        LivestreamModel SelectedLivestream { get; set; }

        bool CanRefreshLivestreams { get; }
        
        event EventHandler OnlineLivestreamsRefreshComplete;

        Task AddLivestream(LivestreamModel livestreamModel);

        Task ImportFollows(string username);

        /// <summary> Refreshing data for all followed livestreams </summary>
        Task RefreshLivestreams();

        void RemoveLivestream(LivestreamModel livestreamModel);
    }
}