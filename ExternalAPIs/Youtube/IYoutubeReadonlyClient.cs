using System.Threading;
using System.Threading.Tasks;
using ExternalAPIs.Youtube.Dto.QueryRoot;

namespace ExternalAPIs.Youtube
{
    public interface IYoutubeReadonlyClient
    {
        /// <summary> A channel in youtube can have multiple livestreams running at one time </summary>
        Task<SearchLiveVideosRoot> GetLivestreamVideos(string channelId, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary> Discover the channel id given a username </summary>
        Task<string> GetChannelIdFromUsername(string userName, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary> Get the details for a youtube videoid </summary>
        Task<VideoRoot> GetLivestreamDetails(string videoId, CancellationToken cancellationToken = default(CancellationToken));
    }
}