using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExternalAPIs.Beam.Pro.Dto;
using ExternalAPIs.Beam.Pro.Query;

namespace ExternalAPIs.Beam.Pro
{
    public interface IBeamProReadonlyClient
    {
        Task<List<Channel>> GetTopStreams(BeamProPagedQuery pagedQuery, CancellationToken cancellationToken = default(CancellationToken));

        Task<Channel> GetStreamDetails(string channelId, CancellationToken cancellationToken = default(CancellationToken));

        /// <param name="channelId">Must be the "id" of the channel and not the channel name/token</param>
        /// <param name="pagedQuery"></param>
        /// <param name="cancellationToken"></param>
        Task<List<Recording>> GetChannelVideos(int channelId, BeamProPagedQuery pagedQuery, CancellationToken cancellationToken = default(CancellationToken));

        Task<List<KnownGame>> GetKnownGames(KnownGamesPagedQuery query, CancellationToken cancellationToken = default(CancellationToken));
    }
}