using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using ExternalAPIs.Youtube.Dto;
using ExternalAPIs.Youtube.Query;
using static System.String;

namespace ExternalAPIs.Youtube
{
    public class YoutubeReadonlyClient : IYoutubeReadonlyClient
    {
        public async Task<SearchLiveVideosRoot> GetLivestreamVideos(string channelId, CancellationToken cancellationToken = default)
        {
            if (IsNullOrWhiteSpace(channelId)) throw new ArgumentException("Argument is null or whitespace", nameof(channelId));

            var request = RequestConstants.SearchChannelLiveVideos.Replace("{0}", channelId);
            var searchChannelLiveVideos = await HttpClientExtensions.ExecuteRequest<SearchLiveVideosRoot>(request, cancellationToken);

            return searchChannelLiveVideos;
        }

        public async Task<ChannelsRoot> GetChannelDetailsFromIds(List<string> channelIds, CancellationToken cancellationToken = default)
        {
            if (channelIds == null) throw new ArgumentNullException(nameof(channelIds));

            var request = $"{RequestConstants.GetChannels}&id={Join(",", channelIds)}";
            var channelDetails = await HttpClientExtensions.ExecuteRequest<ChannelsRoot>(request, cancellationToken);

            if (channelDetails?.Items?.Count == 0)
                throw new HttpRequestWithStatusException(HttpStatusCode.BadRequest, "Channels not found " + channelIds);

            return channelDetails;
        }

        public async Task<ChannelsRoot> GetChannelDetailsFromHandle(string handle, CancellationToken cancellationToken = default)
        {
            if (IsNullOrWhiteSpace(handle))
                throw new ArgumentException("Argument is null or whitespace", nameof(handle));

            var request = RequestConstants.GetChannelByHandle.Replace("{0}", handle);
            var channelDetails = await HttpClientExtensions.ExecuteRequest<ChannelsRoot>(request, cancellationToken);
            if (channelDetails.Items == null || channelDetails.Items.Count == 0)
                throw new HttpRequestWithStatusException(HttpStatusCode.BadRequest, $"No channel found for handle '{handle}'");

            return channelDetails;
        }

        public async Task<VideosRoot> GetVideosDetails(IReadOnlyCollection<string> videoIds, CancellationToken cancellationToken = default)
        {
            if (videoIds == null) throw new ArgumentNullException(nameof(videoIds));

            var request = $"{RequestConstants.VideoLivestreamDetails}&id={Join(",", videoIds)}";
            var livestreamDetails = await HttpClientExtensions.ExecuteRequest<VideosRoot>(request, cancellationToken);

            if (livestreamDetails?.Items?.Count == 0)
                throw new HttpRequestWithStatusException(HttpStatusCode.BadRequest, "Videos not found " + videoIds);

            return livestreamDetails;
        }

        public async Task<PlaylistItemsRoot> GetPlaylistItems(PlaylistItemsQuery query, CancellationToken cancellationToken = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));

            var request = RequestConstants.GetPlaylistItems.Replace("{0}", query.PlaylistId) + $"&maxResults={query.ItemsPerPage}";
            if (query.PageToken != null) request += $"&pageToken={query.PageToken}";

            var playlistItemsRoot = await HttpClientExtensions.ExecuteRequest<PlaylistItemsRoot>(request, cancellationToken);

            if (playlistItemsRoot == null)
                throw new HttpRequestWithStatusException(HttpStatusCode.BadRequest,
                    $"Playlist not found| Playlist: {query.PlaylistId}, Token: {query.PageToken}");

            return playlistItemsRoot;
        }
    }
}
