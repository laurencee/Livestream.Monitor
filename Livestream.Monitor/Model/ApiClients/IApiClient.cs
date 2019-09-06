using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Caliburn.Micro;
using ExternalAPIs;
using Livestream.Monitor.Model.Monitoring;

namespace Livestream.Monitor.Model.ApiClients
{
    public interface IApiClient
    {
        string ApiName { get; }

        /// <summary> The base url for the provider (always ending with a forward slash '/')  </summary>
        string BaseUrl { get; }

        bool HasChatSupport { get; }

        bool HasVodViewerSupport { get; }

        bool HasTopStreamsSupport { get; }

        bool HasTopStreamGameFilterSupport { get; }

        bool HasUserFollowQuerySupport { get; }

        /// <summary> Check if the user is authorized to interact with this api </summary>
        bool IsAuthorized { get; }

        List<string> VodTypes { get; }

        string LivestreamerAuthorizationArg { get; }

        /// <summary> Execute authorization process for api </summary>
        /// <param name="screen">Screen to activate any prompts/input required during authorization</param>
        /// <returns>True if the user was authorized successfully</returns>
        Task Authorize(IViewAware screen);

        Task<string> GetStreamUrl(string channelId);

        /// <summary> Returns null if <see cref="HasChatSupport"/> is false </summary>
        Task<string> GetChatUrl(string channelId);

        // TODO - consider isolating the add/remove/query logic into some other "monitoring api client" class
        /// <summary> Adds a channel for the api client to follow when <see cref="QueryChannels"/> is called  and immediately queries the channel. </summary>
        /// <param name="newChannel">A <see cref="ChannelIdentifier"/> of the new channel to be added</param>
        Task<List<LivestreamQueryResult>> AddChannel(ChannelIdentifier newChannel);

        /// <summary> Adds a channel for the api client to follow when <see cref="QueryChannels"/> is called but does not immediately query the channel. </summary>
        /// <param name="newChannel"></param>
        void AddChannelWithoutQuerying(ChannelIdentifier newChannel);

        Task RemoveChannel(ChannelIdentifier channelIdentifier);

        /// <summary> Query the api for all followed channels livestreams. Channels to be queried are to be added through <see cref="AddChannel"/> </summary>
        /// <param name="cancellationToken"></param>
        /// <returns>Livestream query results from the channels. Errors from querying will be captured to be examined later. </returns>
        Task<List<LivestreamQueryResult>> QueryChannels(CancellationToken cancellationToken);

        Task<List<VodDetails>> GetVods(VodQuery vodQuery);

        Task<List<LivestreamQueryResult>> GetTopStreams(TopStreamQuery topStreamQuery);

        /// <summary> Gets a list of known game names from the api with an optional game name filter</summary>
        /// <param name="filterGameName">Optional game name filter</param>
        /// <returns>Collection of known games from the api</returns>
        Task<List<KnownGame>> GetKnownGameNames(string filterGameName);

        Task<List<LivestreamQueryResult>> GetUserFollows(string userName);

        /// <summary> Give the api client a chance to initialize/preload data if necessary. Call after adding channels to be monitored. </summary>
        Task Initialize(CancellationToken cancellationToken = default);
    }
}