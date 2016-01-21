using System;
using System.Net;
using System.Threading.Tasks;
using Google.API.Dto;
using HttpCommon;
using static System.String;

namespace Google.API
{
    public class YoutubeReadonlyClient : IYoutubeReadonlyClient
    {
        public async Task<VideoRoot> GetLivestreamDetails(string videoId)
        {
            if (IsNullOrWhiteSpace(videoId)) throw new ArgumentNullException(nameof(videoId));

            var request = $"{RequestConstants.VideoLivestreamDetails}&id={videoId}";
            var livestreamDetails = await HttpClientExtensions.ExecuteRequest<VideoRoot>(request);

            request = $"{RequestConstants.VideoSnippet}&id={videoId}";
            var snippetDetails = await HttpClientExtensions.ExecuteRequest<VideoRoot>(request);

            if (livestreamDetails?.Items?.Count > 0 && snippetDetails?.Items?.Count > 0)
                livestreamDetails.Items[0].Snippet = snippetDetails.Items[0].Snippet;
            else
            {
                // youtube just returns empty values when no stream was found
                throw new HttpRequestWithStatusException(HttpStatusCode.NotFound, "Channel not found " + videoId);
            }

            return livestreamDetails;
        }
    }
}
