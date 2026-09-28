using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ExternalAPIs.Youtube.Dto;
using ExternalAPIs.Youtube.Query;
using static System.String;

namespace ExternalAPIs.Youtube
{
    public class YoutubeReadonlyClient : IYoutubeReadonlyClient
    {
        private static readonly string ApiKey = Encoding.UTF8.GetString(Convert.FromBase64String(RequestConstants.API_KEY));

        public async Task<List<Video>> GetLivestreamVideos(string channelId, CancellationToken cancellationToken = default)
        {
            if (IsNullOrWhiteSpace(channelId)) throw new ArgumentException("Argument is null or whitespace", nameof(channelId));

            const string channelIdPrefix = "UC";
            const string livestreamPlaylistPrefix = "UULV";

            // Search can omit active broadcasts. UULV is YouTube's undocumented livestream-only playlist,
            // including past broadcasts; video details distinguish those currently live.
            // https://github.com/DialmasterOrg/Youtarr/blob/main/server/modules/youtubeApi/constants.js
            var playlistId = livestreamPlaylistPrefix + channelId.Substring(channelIdPrefix.Length);

            // Limit discovery to the first page: scanning archived streams can exceed the refresh timeout.
            const int maxLivestreamCandidates = 50;
            var query = new PlaylistItemsQuery(playlistId) { ItemsPerPage = maxLivestreamCandidates };
            var playlistItems = await GetPlaylistItems(query, cancellationToken);
            var videoIds = playlistItems.Items.Select(x => x.ContentDetails.VideoId).ToList();

            if (videoIds.Count == 0) return new List<Video>();

            var videos = await GetVideosDetails(videoIds, cancellationToken);
            return videos.Items.Where(x => x.Snippet?.LiveBroadcastContent == "live").ToList();
        }

        public async Task<ChannelsRoot> GetChannelDetailsFromIds(List<string> channelIds, CancellationToken cancellationToken = default)
        {
            if (channelIds == null) throw new ArgumentNullException(nameof(channelIds));

            var request = $"{RequestConstants.GetChannels}&id={Join(",", channelIds)}";
            var channelDetails = await ExecuteRequest<ChannelsRoot>(request, cancellationToken);

            if (channelDetails?.Items?.Count == 0)
                throw new HttpRequestWithStatusException(HttpStatusCode.BadRequest, "Channels not found " + channelIds);

            return channelDetails;
        }

        public async Task<ChannelsRoot> GetChannelDetailsFromHandle(string handle, CancellationToken cancellationToken = default)
        {
            if (IsNullOrWhiteSpace(handle))
                throw new ArgumentException("Argument is null or whitespace", nameof(handle));

            var request = RequestConstants.GetChannelByHandle.Replace("{0}", handle);
            var channelDetails = await ExecuteRequest<ChannelsRoot>(request, cancellationToken);
            if (channelDetails.Items == null || channelDetails.Items.Count == 0)
                throw new HttpRequestWithStatusException(HttpStatusCode.BadRequest, $"No channel found for handle '{handle}'");

            return channelDetails;
        }

        public async Task<VideosRoot> GetVideosDetails(IReadOnlyCollection<string> videoIds, CancellationToken cancellationToken = default)
        {
            if (videoIds == null) throw new ArgumentNullException(nameof(videoIds));

            var request = $"{RequestConstants.VideoLivestreamDetails}&id={Join(",", videoIds)}";
            var livestreamDetails = await ExecuteRequest<VideosRoot>(request, cancellationToken);

            if (livestreamDetails?.Items?.Count == 0)
                throw new HttpRequestWithStatusException(HttpStatusCode.BadRequest, "Videos not found " + videoIds);

            return livestreamDetails;
        }

        public async Task<PlaylistItemsRoot> GetPlaylistItems(PlaylistItemsQuery query, CancellationToken cancellationToken = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));

            var request = RequestConstants.GetPlaylistItems.Replace("{0}", query.PlaylistId) + $"&maxResults={query.ItemsPerPage}";
            if (query.PageToken != null) request += $"&pageToken={query.PageToken}";

            var playlistItemsRoot = await ExecuteRequest<PlaylistItemsRoot>(request, cancellationToken);

            if (playlistItemsRoot == null)
                throw new HttpRequestWithStatusException(HttpStatusCode.BadRequest,
                    $"Playlist not found| Playlist: {query.PlaylistId}, Token: {query.PageToken}");

            return playlistItemsRoot;
        }

        private static Task<T> ExecuteRequest<T>(string url, CancellationToken cancellationToken)
        {
            url = $"{url}&key={ApiKey}";
            return HttpClientExtensions.ExecuteRequest<T>(url, cancellationToken);
        }
    }
}
