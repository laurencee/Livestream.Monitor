using System;
using System.Linq;
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

        public async Task<string> GetChannelIdFromChannelName(string channelName, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (IsNullOrWhiteSpace(channelName))
                throw new ArgumentException("Argument is null or whitespace", nameof(channelName));

            var request = RequestConstants.GetChannelIdByName.Replace("{0}", channelName);
            var channelDetails = await HttpClientExtensions.ExecuteRequest<GetChannelIdByNameRoot>(request, cancellationToken);
            if (channelDetails.Items == null || channelDetails.Items.Count == 0)
                throw new HttpRequestWithStatusException(HttpStatusCode.BadRequest, "Channel name not found " + channelName);

            return channelDetails.Items?.FirstOrDefault()?.Id;
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
