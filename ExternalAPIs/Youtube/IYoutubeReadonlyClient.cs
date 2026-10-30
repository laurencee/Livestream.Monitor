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
        /// Gets the video IDs of the first <paramref name="maxEntries"/> entries of a channel's livestream playlist, newest
        /// first, leaving out entries without one.
        /// </summary>
        Task<List<string>> GetLivestreamPlaylistVideoIds(string channelId, int maxEntries, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets those of at most 50 <paramref name="videoIds"/> that are live broadcasts now, with only the ID, title, channel
        /// title, broadcast state, default, standard and maxres thumbnail URLs, actual start time, live chat ID and viewer
        /// count; a channel in youtube can have multiple livestreams running at one time.
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