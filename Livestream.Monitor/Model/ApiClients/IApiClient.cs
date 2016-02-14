using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExternalAPIs.TwitchTv.Query;
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

        bool HasUserFollowQuerySupport { get; }

        List<string> VodTypes { get; }

        string GetStreamUrl(string channelId);

        /// <summary> Returns null if <see cref="HasChatSupport"/> is false </summary>
        string GetChatUrl(string channelId);

        /// <summary> Query the api for livestreams </summary>
        /// <param name="channelIdentifiers">A collection of identifiers for livestream channels</param>
        /// <param name="cancellationToken"></param>
        /// <returns>Livestream query results from the channels. Errors from querying will be captured to be examined later. </returns>
        Task<List<LivestreamQueryResult>> GetLivestreams(List<ChannelIdentifier> channelIdentifiers, CancellationToken cancellationToken);

        Task<List<VodDetails>> GetVods(VodQuery vodQuery);

        Task<List<LivestreamQueryResult>> GetTopStreams(TopStreamQuery topStreamQuery);

        /// <summary> Gets a list of known game names from the api with an optional game name filter</summary>
        /// <param name="filterGameName">Optional game name filter</param>
        /// <returns>Collection of known games from the api</returns>
        Task<List<KnownGame>> GetKnownGameNames(string filterGameName);

        Task<List<LivestreamQueryResult>> GetUserFollows(string userName);
    }
}