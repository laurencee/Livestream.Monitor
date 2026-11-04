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

        public async Task<List<string>> GetLivestreamPlaylistVideoIds(string channelId, int maxEntries, CancellationToken cancellationToken = default)
        {
            if (IsNullOrWhiteSpace(channelId)) throw new ArgumentException("Argument is null or whitespace", nameof(channelId));

            const string ChannelIdPrefix = "UC";
            const string LivestreamPlaylistPrefix = "UULV";

            // Search can omit active broadcasts. UULV is YouTube's undocumented livestream-only playlist,
            // including past broadcasts; video details distinguish those currently live.
            // https://github.com/DialmasterOrg/Youtarr/blob/main/server/modules/youtubeApi/constants.js
            var playlistId = LivestreamPlaylistPrefix + channelId.Substring(ChannelIdPrefix.Length);

            var query = new PlaylistItemsQuery(playlistId) { ItemsPerPage = maxEntries };
            var playlistItems = await GetPlaylistItems(query, cancellationToken);

            return playlistItems.Items.Select(x => x.ContentDetails.VideoId).Where(id => id != null).ToList();
        }

        public async Task<List<Video>> GetLiveVideos(IReadOnlyCollection<string> videoIds, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(videoIds);

            // Request only the fields used to display livestreams.
            // https://developers.google.com/youtube/v3/getting-started#fields
            const string LiveVideoFields =
                "items(id,snippet(channelTitle,title,liveBroadcastContent,thumbnails(default/url,standard/url,maxres/url)),liveStreamingDetails(actualStartTime,activeLiveChatId,concurrentViewers))";
            var request = $"{RequestConstants.VideoLiveStatus}&fields={LiveVideoFields}&id={Join(",", videoIds)}";
            var videos = await ExecuteRequest<VideosRoot>(request, cancellationToken);

            // Deleted and private videos are left out, so the response can have no items.
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

        public Task<VideosRoot> GetVideosDetails(IReadOnlyCollection<string> videoIds, CancellationToken cancellationToken = default) =>
            GetVideos(RequestConstants.VideoLivestreamDetails, videoIds, cancellationToken);

        private static async Task<VideosRoot> GetVideos(string videosUrl, IReadOnlyCollection<string> videoIds, CancellationToken cancellationToken)
        {
            if (videoIds == null) throw new ArgumentNullException(nameof(videoIds));

            var request = $"{videosUrl}&id={Join(",", videoIds)}";
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
