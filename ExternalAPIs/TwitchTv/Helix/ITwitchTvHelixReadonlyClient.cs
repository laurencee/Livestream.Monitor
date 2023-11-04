using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExternalAPIs.TwitchTv.Helix.Dto;
using ExternalAPIs.TwitchTv.Helix.Query;

namespace ExternalAPIs.TwitchTv.Helix
{
    public interface ITwitchTvHelixReadonlyClient
    {
        Task<List<FollowedChannel>> GetFollowedChannels(string userId, CancellationToken cancellationToken = default);

        Task<StreamsRoot> GetStreams(GetStreamsQuery getStreamsQuery, CancellationToken cancellationToken = default);

        Task<List<TwitchCategory>> GetTopGames(CancellationToken cancellationToken = default);

        Task<User> GetUserByUsername(string username, CancellationToken cancellationToken = default);

        Task<List<User>> GetUsers(GetUsersQuery getUsersQuery, CancellationToken cancellationToken = default);

        Task<VideosRoot> GetVideos(GetVideosQuery getVideosQuery, CancellationToken cancellationToken = default);

        Task<List<Game>> GetGames(GetGamesQuery getGamesQuery, CancellationToken cancellationToken = default);

        Task<List<TwitchCategory>> SearchCategories(string searchCategory, CancellationToken cancellationToken = default);
        
        void SetAccessToken(string accessToken);
    }
}