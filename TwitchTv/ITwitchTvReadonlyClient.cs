using System.Threading.Tasks;
using TwitchTv.Dto;

namespace TwitchTv
{
    public interface ITwitchTvReadonlyClient
    {
        Task<UserFollows> GetUserFollows(string username);
        Task<Stream> GetStreamDetails(string streamName);
        Task<Channel> GetChannelDetails(string username);
    }
}