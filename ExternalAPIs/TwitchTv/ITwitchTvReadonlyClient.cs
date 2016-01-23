using System.Collections.Generic;
using System.Threading.Tasks;
using ExternalAPIs.TwitchTv.Dto;
using ExternalAPIs.TwitchTv.Dto.QueryRoot;
using ExternalAPIs.TwitchTv.Query;

namespace ExternalAPIs.TwitchTv
{
    public interface ITwitchTvReadonlyClient
    {
        Task<UserFollows> GetUserFollows(string username);

        Task<Channel> GetChannelDetails(string streamName);

        Task<Stream> GetStreamDetails(string streamName);

        Task<List<Stream>> GetStreamsDetails(List<string> streamNames);

        Task<List<Game>> GetTopGames();

        Task<List<Stream>> SearchStreams(string streamName);

        Task<List<Game>> SearchGames(string gameName);

        /// <summary> Gets the top streams </summary>
        Task<List<Stream>> GetTopStreams(TopStreamQuery topStreamQuery);

        Task<List<Video>> GetChannelVideos(ChannelVideosQuery channelVideosQuery);
    }
}