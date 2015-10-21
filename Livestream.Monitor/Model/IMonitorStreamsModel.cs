using System;
using System.Threading.Tasks;
using Caliburn.Micro;

namespace Livestream.Monitor.Model
{
    public interface IMonitorStreamsModel : INotifyPropertyChangedEx
    {
        /// <summary> The list of followed channels, will be initialized on first access. </summary>
        BindableCollection<ChannelData> FollowedChannels { get; }

        bool CanRefreshChannels { get; }

        event EventHandler OnlineChannelsRefreshComplete;

        Task AddStream(ChannelData channelData);

        Task ImportFollows(string username);

        /// <summary> Refreshing data for all followed channels </summary>
        Task RefreshChannels();

        void RemoveChannel(ChannelData channelData);
    }
}