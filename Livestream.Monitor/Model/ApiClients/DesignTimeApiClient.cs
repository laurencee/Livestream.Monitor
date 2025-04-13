using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Caliburn.Micro;
using ExternalAPIs;
using Livestream.Monitor.Model.Monitoring;

namespace Livestream.Monitor.Model.ApiClients
{
    public class DesignTimeApiClient : IApiClient
    {
        public string ApiName { get; } = "DesignTimeApiClient";
        public string BaseUrl { get; }
        public bool HasChatSupport { get; }
        public bool HasVodViewerSupport { get; }
        public bool HasTopStreamsSupport { get; }
        public bool HasTopStreamGameFilterSupport { get; }
        public bool HasFollowedChannelsQuerySupport { get; }
        public bool IsAuthorized { get; }
        public List<string> VodTypes { get; }
        public string LivestreamerAuthorizationArg { get; }
        public Task Authorize(IViewAware screen)
        {
            throw new NotImplementedException();
        }

        public Task<string> GetStreamUrl(LivestreamModel livestreamModel)
        {
            throw new NotImplementedException();
        }

        public Task<string> GetChatUrl(LivestreamModel livestreamModel)
        {
            throw new NotImplementedException();
        }

        public Task<List<LivestreamQueryResult>> AddChannel(ChannelIdentifier newChannel)
        {
            throw new NotImplementedException();
        }

        public void AddChannelWithoutQuerying(ChannelIdentifier newChannel)
        {
            throw new NotImplementedException();
        }

        public Task RemoveChannel(ChannelIdentifier channelIdentifier)
        {
            throw new NotImplementedException();
        }

        public Task<List<LivestreamQueryResult>> QueryChannels(CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<IReadOnlyCollection<VodDetails>> GetVods(VodQuery vodQuery)
        {
            throw new NotImplementedException();
        }

        public Task<TopStreamsResponse> GetTopStreams(TopStreamQuery topStreamQuery)
        {
            throw new NotImplementedException();
        }

        public Task<List<KnownGame>> GetKnownGameNames(string filterGameName)
        {
            throw new NotImplementedException();
        }

        public Task<List<LivestreamQueryResult>> GetFollowedChannels(string userName)
        {
            throw new NotImplementedException();
        }

        public Task<InitializeApiClientResult> Initialize(CancellationToken cancellationToken = default) =>
            Task.FromResult(new InitializeApiClientResult());
    }
}