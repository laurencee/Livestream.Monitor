using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExternalAPIs.Youtube.Dto;
using ExternalAPIs.Youtube.Query;

namespace ExternalAPIs.Youtube
{
    public interface IYoutubeReadonlyClient
    {
        /// <summary>
        /// Gets video IDs from the first <paramref name="maxEntries"/> entries of the channel's livestream playlist, newest first.
        /// Entries without a video ID are skipped.
        /// </summary>
        Task<List<string>> GetLivestreamPlaylistVideoIds(string channelId, int maxEntries, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets live broadcasts from up to 50 video IDs, with only the fields needed to display livestreams.
        /// A YouTube channel can have multiple livestreams running at once.
        /// </summary>
        Task<List<Video>> GetLiveVideos(IReadOnlyCollection<string> videoIds, CancellationToken cancellationToken = default);

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