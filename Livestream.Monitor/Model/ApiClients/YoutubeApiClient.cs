using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Caching;
using System.Threading;
using System.Threading.Tasks;
using ExternalAPIs.TwitchTv.Query;
using ExternalAPIs.Youtube;
using Livestream.Monitor.Core;
using Livestream.Monitor.Model.Monitoring;

namespace Livestream.Monitor.Model.ApiClients
{
    public class YoutubeApiClient : IApiClient
    {
        public const string API_NAME = "youtube";

        private readonly IYoutubeReadonlyClient youtubeClient;
        private readonly MemoryCache cache = new MemoryCache(API_NAME);

        public YoutubeApiClient(IYoutubeReadonlyClient youtubeClient)
        {
            if (youtubeClient == null) throw new ArgumentNullException(nameof(youtubeClient));
            this.youtubeClient = youtubeClient;
        }

        public string ApiName => API_NAME;

        public string BaseUrl => @"https://www.youtube.com/";

        public bool HasChatSupport => true;

        public bool HasVodViewerSupport => false;

        public bool HasTopStreamsSupport => false;

        public bool HasUserFollowQuerySupport => false;

        public List<string> VodTypes { get; } = new List<string>();

        public string GetStreamUrl(string channelId)
        {
            if (string.IsNullOrWhiteSpace(channelId)) throw new ArgumentNullException(nameof(channelId));

            return $"{BaseUrl}watch?v={channelId}";
        }

        public string GetChatUrl(string channelId)
        {
            if (string.IsNullOrWhiteSpace(channelId)) throw new ArgumentNullException(nameof(channelId));

            // the '&from_gaming=1' prevents the annoying popup message appearing at the top of the chat window
            // not all youtube streams have chat support as chat can be disabled for a stream, need to see if there's an api call that provides that info
            return $"{BaseUrl}live_chat?v={channelId}&dark_theme=1&is_popout=1&from_gaming=1";
        }

        public async Task<List<LivestreamQueryResult>> GetLivestreams(List<ChannelIdentifier> channelIdentifiers, CancellationToken cancellationToken)
        {
            var list = await channelIdentifiers.ExecuteInParallel(async channelIdentifier =>
            {
                var queryResults = new List<LivestreamQueryResult>();
                var channelId = await GetChannelIdByChannelName(channelIdentifier.ChannelId);
                if (channelId == null) return queryResults;
                
                try
                {
                    var videoIds = await GetVideoIdsByChannelId(channelId);
                    var livestreamModels = await GetLivestreamModels(videoIds);
                    queryResults.AddRange(livestreamModels.Select(x => new LivestreamQueryResult(channelIdentifier)
                    {
                        LivestreamModel = x,
                    }));
                }
                catch (Exception ex)
                {
                    queryResults.Add(new LivestreamQueryResult(channelIdentifier)
                    {
                        FailedQueryException = new FailedQueryException(channelIdentifier, ex)
                    });
                }

                return queryResults;
            }, Constants.HalfRefreshPollingTime, cancellationToken);

            return list.SelectMany(x => x).ToList();
        }

        public Task<List<VodDetails>> GetVods(VodQuery vodQuery)
        {
            return Task.FromResult(new List<VodDetails>());
        }

        public Task<List<LivestreamQueryResult>> GetTopStreams(TopStreamQuery topStreamQuery)
        {
            throw new NotImplementedException();
        }

        public Task<List<KnownGame>> GetKnownGameNames(string filterGameName)
        {
            throw new NotImplementedException();
        }

        public Task<List<LivestreamQueryResult>> GetUserFollows(string userName)
        {
            throw new NotImplementedException();
        }

        private async Task<string> GetChannelIdByChannelName(string channelName)
        {
            string channelId = (string)cache.Get(channelName);
            if (channelId != null)
                return channelId;

            try
            {
                channelId = await youtubeClient.GetChannelIdFromChannelName(channelName);
                // the id will never change so cache it forever once it's found
                cache.Add(channelName, channelId, DateTimeOffset.MaxValue);
                return channelId;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private async Task<List<string>> GetVideoIdsByChannelId(string channelId)
        {
            var searchLiveVideosRoot = await youtubeClient.GetLivestreamVideos(channelId);
            var videoIds = searchLiveVideosRoot.Items?.Select(x => x.Id.VideoId).ToList();

            if (videoIds == null)
                return new List<string>();

            return videoIds;
        }

        private async Task<List<LivestreamModel>> GetLivestreamModels(List<string> videoIds)
        {
            var livestreamModels = new List<LivestreamModel>();

            foreach (var videoId in videoIds)
            {
                var videoRoot = await youtubeClient.GetLivestreamDetails(videoId);
                var livestreamDetails = videoRoot.Items?.FirstOrDefault()?.LiveStreamingDetails;
                if (livestreamDetails == null) continue;

                var snippet = videoRoot.Items?.FirstOrDefault()?.Snippet;
                if (snippet == null) continue;

                var livestreamModel = new LivestreamModel(videoId, new ChannelIdentifier(this, snippet.ChannelId)) { Live = snippet.LiveBroadcastContent != "none" };
                if (!livestreamModel.Live) continue;

                livestreamModel.DisplayName = snippet.ChannelTitle;
                livestreamModel.Description = snippet.Title;
                livestreamModel.ThumbnailUrls = new ThumbnailUrls()
                {
                    Small = snippet.Thumbnails?.Standard?.Url,
                    Large = snippet.Thumbnails?.High?.Url,
                    Medium = snippet.Thumbnails?.Medium?.Url
                };

                livestreamModel.Viewers = livestreamDetails.ConcurrentViewers;

                if (livestreamDetails.ActualStartTime.HasValue)
                {
                    livestreamModel.StartTime = livestreamDetails.ActualStartTime.Value;
                    livestreamModels.Add(livestreamModel);
                }
            }

            return livestreamModels;
        }
    }
}