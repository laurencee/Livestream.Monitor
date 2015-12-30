using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Google.API.Dto;
using Newtonsoft.Json;
using static System.String;

namespace Google.API
{
    public class GoogleVideoReadonlyClient : IGoogleVideoReadonlyClient
    {
        public async Task<VideoRoot> GetLivestreamDetails(string videoId)
        {
            if (IsNullOrWhiteSpace(videoId)) throw new ArgumentNullException(nameof(videoId));

            var request = $"{RequestConstants.VideoLivestreamDetails}&id={videoId}";
            var livestreamDetails = await ExecuteRequest<VideoRoot>(request);

            request = $"{RequestConstants.VideoSnippet}&id={videoId}";
            var snippetDetails = await ExecuteRequest<VideoRoot>(request);

            if (livestreamDetails?.Items?.Count > 0 && snippetDetails?.Items?.Count > 0)
                livestreamDetails.Items[0].Snippet = snippetDetails.Items[0].Snippet;

            return livestreamDetails;
        }

        private async Task<T> ExecuteRequest<T>(string request)
        {
            // we create a new client each time as it will execute much faster (at the expense of some additional memory)
            using (HttpClient httpClient = new HttpClient(new HttpClientHandler()
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            }))
            {
                var responseString = await httpClient.GetStringAsync(request);
                return JsonConvert.DeserializeObject<T>(responseString);
            }
        }
    }
}
