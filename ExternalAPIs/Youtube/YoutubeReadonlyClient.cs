using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using ExternalAPIs.Youtube.Dto.QueryRoot;
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

        public async Task<VideosRoot> GetVideosDetails(IReadOnlyCollection<string> videoIds, CancellationToken cancellationToken = default)
        {
            if (videoIds == null) throw new ArgumentNullException(nameof(videoIds));

            var request = $"{RequestConstants.VideoLivestreamDetails}&id={Join(",", videoIds)}";
            var livestreamDetails = await HttpClientExtensions.ExecuteRequest<VideosRoot>(request, cancellationToken);

            if (livestreamDetails?.Items?.Count == 0)
                throw new HttpRequestWithStatusException(HttpStatusCode.BadRequest, "Channel not found " + videoIds);

            return livestreamDetails;
        }
    }
}
