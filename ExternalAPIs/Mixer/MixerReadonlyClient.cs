using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExternalAPIs.Mixer.Dto;
using ExternalAPIs.Mixer.Query;

namespace ExternalAPIs.Mixer
{
    public class MixerReadonlyClient : IMixerReadonlyClient
    {
        public const int DefaultItemsPerQuery = 50;

        /// <param name="channelId">Must be the "id" of the channel and not the channel name/token</param>
        /// <param name="pagedQuery"></param>
        /// <param name="cancellationToken"></param>
        public async Task<List<Recording>> GetChannelVideos(int channelId, MixerPagedQuery pagedQuery, CancellationToken cancellationToken = default(CancellationToken))
        {
            var request = $"{RequestConstants.Videos.Replace("{0}", channelId.ToString())}?page={pagedQuery.Skip}&limit={pagedQuery.Take}";
            return await HttpClientExtensions.ExecuteRequest<List<Recording>>(request, cancellationToken);
        }

        public async Task<Channel> GetStreamDetails(string channelId, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(channelId)) throw new ArgumentNullException(nameof(channelId));

            var request = RequestConstants.Channels + "/" + channelId;
            return await HttpClientExtensions.ExecuteRequest<Channel>(request, cancellationToken);
        }

        public async Task<List<Channel>> GetTopStreams(MixerPagedQuery pagedQuery, CancellationToken cancellationToken = default(CancellationToken))
        {
            var request = RequestConstants.Channels + $"?order=viewersCurrent:DESC&page={pagedQuery.Skip}&limit={pagedQuery.Take}";
            return await HttpClientExtensions.ExecuteRequest<List<Channel>>(request, cancellationToken);
        }

        public async Task<List<KnownGame>> GetKnownGames(KnownGamesPagedQuery query, CancellationToken cancellationToken = default(CancellationToken))
        {
            var request = RequestConstants.Types + "?order=viewersCurrent:DESC&limit=10";
            if (!string.IsNullOrEmpty(query.GameName))
                request += $"&query={query.GameName}";

            return await HttpClientExtensions.ExecuteRequest<List<KnownGame>>(request, cancellationToken);
        }
    }
}