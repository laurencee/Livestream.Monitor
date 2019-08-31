using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExternalAPIs.TwitchTv.Helix.Dto;
using ExternalAPIs.TwitchTv.Helix.Query;

namespace ExternalAPIs.TwitchTv.Helix
{
    public interface ITwitchTvReadonlyClient
    {
        Task<List<UserFollow>> GetUserFollows(string userId, CancellationToken cancellationToken = default);

        Task<List<Stream>> GetStreams(GetStreamsQuery getStreamsQuery, CancellationToken cancellationToken = default);

        Task<List<TopGame>> GetTopGames(CancellationToken cancellationToken = default);

        Task<List<User>> GetUsers(GetUsersQuery getUsersQuery, CancellationToken cancellationToken = default);

        Task<List<Video>> GetVideos(GetVideosQuery getVideosQuery, CancellationToken cancellationToken = default);

        Task<List<Game>> GetGames(GetGamesQuery getGamesQuery, CancellationToken cancellationToken = default);
    }
}