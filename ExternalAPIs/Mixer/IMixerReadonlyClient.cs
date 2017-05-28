using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExternalAPIs.Mixer.Dto;
using ExternalAPIs.Mixer.Query;

namespace ExternalAPIs.Mixer
{
    public interface IMixerReadonlyClient
    {
        Task<List<Channel>> GetTopStreams(MixerPagedQuery pagedQuery, CancellationToken cancellationToken = default(CancellationToken));

        Task<Channel> GetStreamDetails(string channelId, CancellationToken cancellationToken = default(CancellationToken));

        /// <param name="channelId">Must be the "id" of the channel and not the channel name/token</param>
        /// <param name="pagedQuery"></param>
        /// <param name="cancellationToken"></param>
        Task<List<Recording>> GetChannelVideos(int channelId, MixerPagedQuery pagedQuery, CancellationToken cancellationToken = default(CancellationToken));

        Task<List<KnownGame>> GetKnownGames(KnownGamesPagedQuery query, CancellationToken cancellationToken = default(CancellationToken));
    }
}