using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExternalAPIs.Youtube.Dto.QueryRoot;

namespace ExternalAPIs.Youtube
{
    public interface IYoutubeReadonlyClient
    {
        /// <summary> A channel in youtube can have multiple livestreams running at one time </summary>
        Task<SearchLiveVideosRoot> GetLivestreamVideos(string channelId, CancellationToken cancellationToken = default);

        /// <summary> Lookup channel details from a handle e.g. @GoogleDevelopers </summary>
        Task<GetChannelsRoot> GetChannelDetailsFromHandle(string handle, CancellationToken cancellationToken = default);

        /// <summary> Get the details for a youtube videoid </summary>
        Task<VideosRoot> GetVideosDetails(IReadOnlyCollection<string> videoIds, CancellationToken cancellationToken = default);
    }
}