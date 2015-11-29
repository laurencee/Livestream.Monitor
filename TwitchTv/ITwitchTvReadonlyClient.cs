using System.Collections.Generic;
using System.Threading.Tasks;
using TwitchTv.Dto;
using TwitchTv.Dto.QueryRoot;

namespace TwitchTv
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
    }
}