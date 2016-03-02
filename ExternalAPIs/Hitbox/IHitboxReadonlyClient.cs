using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExternalAPIs.Hitbox.Dto;
using ExternalAPIs.Hitbox.Query;

namespace ExternalAPIs.Hitbox
{
    public interface IHitboxReadonlyClient
    {
        Task<List<Livestream>> GetTopStreams(TopStreamsQuery topStreamsQuery, CancellationToken cancellationToken = default(CancellationToken));

        Task<Mediainfo> GetStreamDetails(string streamId, CancellationToken cancellationToken = default(CancellationToken));

        Task<Livestream> GetChannelDetails(string channelName, CancellationToken cancellationToken = default(CancellationToken));

        Task<List<Video>> GetChannelVideos(ChannelVideosQuery channelVideosQuery, CancellationToken cancellationToken = default(CancellationToken));

        Task<List<Following>> GetUserFollows(string username, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary> Gets top games current on hitbox with an optional game name filter </summary>
        /// <param name="gameName">Optional value to filter games by</param>
        /// <param name="cancellationToken"></param>
        Task<List<Category>> GetTopGames(string gameName = null, CancellationToken cancellationToken = default(CancellationToken));
    }
}