using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Livestream.Monitor.Core;
using Livestream.Monitor.Model.ApiClients;

namespace Livestream.Monitor.Model.Monitoring
{
    public interface IMonitorStreamsModel
    {
        /// <summary>Followed streams, populated during initialization and updated as channels change.</summary>
        ObservableCollection<LivestreamModel> Livestreams { get; }

        bool Initialised { get; }

        bool CanRefreshLivestreams { get; }

        event EventHandler LivestreamsRefreshComplete;

        event Action<LivestreamNotification> CategoryChangeNotification;

        /// <summary>
        /// Raised after applying a provider's results, before <see cref="LivestreamsRefreshComplete"/>.
        /// </summary>
        event EventHandler ProviderRefreshComplete;

        /// <summary>Starts loading followed channels and querying providers before the window opens.</summary>
        void BeginInitialQuery();

        /// <summary>
        /// Allows for loading of livestreams and any other initialization tasks before refreshes can take place.
        /// Returns after applying the primary queries and any required rechecks. Complete queries may still be running;
        /// their failures are not reported. <see cref="LivestreamsRefreshComplete"/> marks the end of the first refresh.
        /// </summary>
        Task Initialize(IUserDialogs dialogs, CancellationToken cancellationToken = default);

        Task AddLivestream(ChannelIdentifier channelIdentifier, IUserDialogs dialogs);

        Task RemoveLivestream(ChannelIdentifier channelIdentifier);

        Task ImportFollows(string username, IApiClient apiClient, IUserDialogs dialogs);

        /// <summary> Refreshing data for all followed livestreams </summary>
        Task RefreshLivestreams();

        /// <summary> Prevent a specific error message from raising query failed exceptions </summary>
        /// <param name="errorToIgnore">The error message from a query failure to ignore</param>
        void IgnoreQueryFailure(string errorToIgnore);
    }
}
