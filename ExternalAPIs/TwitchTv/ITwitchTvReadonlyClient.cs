using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExternalAPIs.TwitchTv.Dto;
using ExternalAPIs.TwitchTv.Dto.QueryRoot;
using ExternalAPIs.TwitchTv.Query;

namespace ExternalAPIs.TwitchTv
{
    public interface ITwitchTvReadonlyClient
    {
        Task<UserFollows> GetUserFollows(string username, CancellationToken cancellationToken = default(CancellationToken));

        Task<Channel> GetChannelDetails(string streamName, CancellationToken cancellationToken = default(CancellationToken));

        Task<Stream> GetStreamDetails(string streamName, CancellationToken cancellationToken = default(CancellationToken));

        Task<List<Stream>> GetStreamsDetails(IEnumerable<string> streamNames, CancellationToken cancellationToken = default(CancellationToken));

        Task<List<Game>> GetTopGames(CancellationToken cancellationToken = default(CancellationToken));

        Task<List<Stream>> SearchStreams(string streamName, CancellationToken cancellationToken = default(CancellationToken));

        Task<List<Game>> SearchGames(string gameName, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary> Gets the top streams </summary>
        Task<List<Stream>> GetTopStreams(TopStreamQuery topStreamQuery, CancellationToken cancellationToken = default(CancellationToken));

        Task<List<Video>> GetChannelVideos(ChannelVideosQuery channelVideosQuery, CancellationToken cancellationToken = default(CancellationToken));
    }
}