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

        /// <summary> Gets the top streams </summary>
        /// <param name="skip">Number of streams to skip</param>
        /// <param name="take">Number of streams to take (max 100)</param>
        Task<List<Stream>> GetTopStreams(int skip, int take = 25);

        /// <summary> Gets the top 100 streams by <paramref name="gameName"/> </summary>
        Task<List<Stream>> GetTopStreamsByGame(string gameName);
    }
}