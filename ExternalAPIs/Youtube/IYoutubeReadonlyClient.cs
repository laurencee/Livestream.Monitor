using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExternalAPIs.Youtube.Dto;
using ExternalAPIs.Youtube.Query;

namespace ExternalAPIs.Youtube
{
    public interface IYoutubeReadonlyClient
    {
        /// <summary> A channel in youtube can have multiple livestreams running at one time </summary>
        Task<SearchLiveVideosRoot> GetLivestreamVideos(string channelId, CancellationToken cancellationToken = default);

        /// <summary> Lookup channel details channel ids </summary>
        Task<ChannelsRoot> GetChannelDetailsFromIds(List<string> channelIds, CancellationToken cancellationToken = default);

        /// <summary> Lookup channel details from a handle e.g. @GoogleDevelopers </summary>
        Task<ChannelsRoot> GetChannelDetailsFromHandle(string handle, CancellationToken cancellationToken = default);

        /// <summary> Get the details for a youtube videoid </summary>
        Task<VideosRoot> GetVideosDetails(IReadOnlyCollection<string> videoIds, CancellationToken cancellationToken = default);

        /// <summary> Get the videos in a playlist </summary>
        Task<PlaylistItemsRoot> GetPlaylistItems(PlaylistItemsQuery query, CancellationToken cancellationToken = default);
    }
}