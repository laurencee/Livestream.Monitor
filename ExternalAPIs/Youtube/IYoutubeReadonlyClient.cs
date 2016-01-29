using System.Threading.Tasks;
using ExternalAPIs.Youtube.Dto;
using ExternalAPIs.Youtube.Dto.QueryRoot;

namespace ExternalAPIs.Youtube
{
    public interface IYoutubeReadonlyClient
    {
        /// <summary> A channel in youtube can have multiple livestreams running at one time </summary>
        /// <param name="channelId">A channel id discovered from querying <see cref="GetChannelIdFromChannelName"/></param>
        /// <returns></returns>
        Task<SearchLiveVideosRoot> GetLivestreamVideos(string channelId);

        Task<string> GetChannelIdFromChannelName(string channelName);

        Task<VideoRoot> GetLivestreamDetails(string videoId);
    }
}