using Livestream.Monitor.Core;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExternalAPIs;
using Livestream.Monitor.Model.Monitoring;

namespace Livestream.Monitor.Model.ApiClients
{
    public interface IApiClient
    {
        string ApiName { get; }

        bool HasChatSupport { get; }

        bool HasVodViewerSupport { get; }

        bool HasTopStreamsSupport { get; }

        bool HasTopStreamGameFilterSupport { get; }

        bool HasFollowedChannelsQuerySupport { get; }

        /// <summary> Check if the user is authorized to interact with this api </summary>
        bool IsAuthorized { get; }

        /// <summary> True when <see cref="Authorize"/> can run without prompting the user </summary>
        bool CanAuthorizeWithoutUserInput { get; }

        /// <summary> True when <see cref="QueryChannelsQuickly"/> is faster than <see cref="QueryChannels"/> </summary>
        bool HasQuickQuerySupport { get; }

        List<string> VodTypes { get; }

        /// <summary> Execute authorization process for api </summary>
        /// <param name="screen">Shows any prompts or input needed during authorization</param>
        Task Authorize(IUserDialogs screen);

        Task<string> GetStreamUrl(LivestreamModel livestreamModel);

        Task<string> FindChannelIconUrl(ChannelIdentifier channel, CancellationToken cancellationToken);

        /// <summary> Returns null if <see cref="HasChatSupport"/> is false </summary>
        Task<string> GetChatUrl(LivestreamModel livestreamModel);

        // TODO - don't use ChannelIdentifier at this point as we don't know the actual channelid yet in most cases, we only know the url/slug
        /// <summary> Adds a channel to the channels that <see cref="QueryChannels"/> queries, and queries it immediately. </summary>
        Task<List<LivestreamQueryResult>> AddChannel(ChannelIdentifier newChannel);

        /// <summary> Adds a channel to the channels that <see cref="QueryChannels"/> queries, without querying it now. </summary>
        void AddChannelWithoutQuerying(ChannelIdentifier newChannel);

        Task RemoveChannel(ChannelIdentifier channelIdentifier);

        /// <summary> Queries the api for the livestreams of all followed channels. Add the channels first with <see cref="AddChannel"/>. </summary>
        /// <returns>
        /// The channels' livestream query results. Depending on the client, a failed query is either recorded in the affected
        /// channel's result (<see cref="LivestreamQueryResult.FailedQueryException"/>) or thrown, which fails the whole query.
        /// </returns>
        Task<List<LivestreamQueryResult>> QueryChannels(CancellationToken cancellationToken);

        /// <summary>
        /// Queries followed channels using the faster query when <see cref="HasQuickQuerySupport"/> is true, otherwise the normal query.
        /// The faster query's results may differ from those of <see cref="QueryChannels"/>.
        /// </summary>
        Task<List<LivestreamQueryResult>> QueryChannelsQuickly(CancellationToken cancellationToken);

        Task<IReadOnlyCollection<VodDetails>> GetVods(VodQuery vodQuery);

        Task<TopStreamsResponse> GetTopStreams(TopStreamQuery topStreamQuery);

        /// <summary> Gets a list of known game names from the api with an optional game name filter</summary>
        /// <param name="filterGameName">Optional game name filter</param>
        /// <returns>Collection of known games from the api</returns>
        Task<List<string>> GetKnownGameNames(string filterGameName);

        Task<List<LivestreamQueryResult>> GetFollowedChannels(string userName);

        /// <summary> Give the api client a chance to initialize/preload data if necessary. Call after adding channels to be monitored. </summary>
        Task<InitializeApiClientResult> Initialize(CancellationToken cancellationToken = default);
    }

    public class TopStreamsResponse
    {
        public List<LivestreamModel> LivestreamModels { get; set; } = [];

        public bool HasNextPage { get; set; }
    }
}
