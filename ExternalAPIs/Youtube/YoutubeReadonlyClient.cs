using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using ExternalAPIs.Youtube.Dto.QueryRoot;
using static System.String;

namespace ExternalAPIs.Youtube
{
    public class YoutubeReadonlyClient : IYoutubeReadonlyClient
    {
        public async Task<SearchLiveVideosRoot> GetLivestreamVideos(string channelId, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (IsNullOrWhiteSpace(channelId)) throw new ArgumentException("Argument is null or whitespace", nameof(channelId));

            var request = RequestConstants.SearchChannelLiveVideos.Replace("{0}", channelId);
            var searchChannelLiveVideos = await HttpClientExtensions.ExecuteRequest<SearchLiveVideosRoot>(request, cancellationToken);

            return searchChannelLiveVideos;
        }

        public async Task<GetChannelsRoot> GetChannelDetailsFromHandle(string handle, CancellationToken cancellationToken = default)
        {
            if (IsNullOrWhiteSpace(handle))
                throw new ArgumentException("Argument is null or whitespace", nameof(handle));

            var request = RequestConstants.GetChannelIdByHandle.Replace("{0}", handle);
            var channelDetails = await HttpClientExtensions.ExecuteRequest<GetChannelsRoot>(request, cancellationToken);
            if (channelDetails.Items == null || channelDetails.Items.Count == 0)
                throw new HttpRequestWithStatusException(HttpStatusCode.BadRequest, $"No channel found for handle '{handle}'");

            return channelDetails;
        }

        public async Task<VideoRoot> GetLivestreamDetails(string videoId, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (IsNullOrWhiteSpace(videoId)) throw new ArgumentNullException(nameof(videoId));

            var request = $"{RequestConstants.VideoLivestreamDetails}&id={videoId}";
            var livestreamDetails = await HttpClientExtensions.ExecuteRequest<VideoRoot>(request, cancellationToken);

            request = $"{RequestConstants.VideoSnippet}&id={videoId}";
            var snippetDetails = await HttpClientExtensions.ExecuteRequest<VideoRoot>(request, cancellationToken);

            if (livestreamDetails?.Items?.Count > 0 && snippetDetails?.Items?.Count > 0)
                livestreamDetails.Items[0].Snippet = snippetDetails.Items[0].Snippet;
            else
            {
                // youtube just returns empty values when no stream was found
                throw new HttpRequestWithStatusException(HttpStatusCode.BadRequest, "Channel not found " + videoId);
            }

            return livestreamDetails;
        }
    }
}
