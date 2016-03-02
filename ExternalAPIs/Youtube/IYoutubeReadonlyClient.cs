using System.Threading;
using System.Threading.Tasks;
using ExternalAPIs.Youtube.Dto.QueryRoot;

namespace ExternalAPIs.Youtube
{
    public interface IYoutubeReadonlyClient
    {
        /// <summary> A channel in youtube can have multiple livestreams running at one time </summary>
        /// <param name="channelId">A channel id discovered from querying <see cref="GetChannelIdFromChannelName"/></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task<SearchLiveVideosRoot> GetLivestreamVideos(string channelId, CancellationToken cancellationToken = default(CancellationToken));

        Task<string> GetChannelIdFromChannelName(string channelName, CancellationToken cancellationToken = default(CancellationToken));

        Task<VideoRoot> GetLivestreamDetails(string videoId, CancellationToken cancellationToken = default(CancellationToken));
    }
}