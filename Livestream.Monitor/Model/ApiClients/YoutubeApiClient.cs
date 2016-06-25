using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.Caching;
using System.Threading;
using System.Threading.Tasks;
using ExternalAPIs;
using ExternalAPIs.TwitchTv.Query;
using ExternalAPIs.Youtube;
using ExternalAPIs.Youtube.Dto;
using ExternalAPIs.Youtube.Dto.QueryRoot;
using Livestream.Monitor.Core;
using Livestream.Monitor.Model.Monitoring;

namespace Livestream.Monitor.Model.ApiClients
{
    public class YoutubeApiClient : IApiClient
    {
        public const string API_NAME = "youtube";

        private readonly IYoutubeReadonlyClient youtubeClient;
        private readonly HashSet<ChannelIdentifier> moniteredChannels = new HashSet<ChannelIdentifier>();
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

        public async Task<List<LivestreamQueryResult>> AddChannel(ChannelIdentifier newChannel)
        {
            var queryResults = await QueryChannels(new[] { newChannel }, CancellationToken.None);
            if (queryResults.Any(x => x.IsSuccess))
                moniteredChannels.Add(newChannel);

            return queryResults;
        }

        public void AddChannelWithoutQuerying(ChannelIdentifier newChannel)
        {
            moniteredChannels.Add(newChannel);
        }

        public async Task RemoveChannel(ChannelIdentifier channelIdentifier)
        {
            ChannelIdentifier actualChannel = null;
            foreach (var moniteredChannel in moniteredChannels)
            {
                // map back to the originally added channel (by channel name)
                try
                {
                    var actualChannelId = await GetChannelIdByChannelName(moniteredChannel.ChannelId, CancellationToken.None);
                    var existingChannelIdentifier = moniteredChannels.FirstOrDefault(x => x.ChannelId.IsEqualTo(actualChannelId));
                    if (existingChannelIdentifier != null && Equals(existingChannelIdentifier, channelIdentifier))
                    {
                        actualChannel = moniteredChannel;
                        break;
                    }
                }
                // we're being careful to just address this specific error to avoid removing the wrong channel due some network/api error
                catch (Exception e) when(e.Message.StartsWith("Channel name not found"))
                {
                    actualChannel = moniteredChannel;
                }
            }
            
            if (actualChannel != null) moniteredChannels.Remove(actualChannel);
        }

        public Task<List<LivestreamQueryResult>> QueryChannels(CancellationToken cancellationToken)
        {
            return QueryChannels(moniteredChannels, cancellationToken);
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

        private async Task<string> GetChannelIdByChannelName(string channelName, CancellationToken cancellationToken)
        {
            string channelId = (string)cache.Get(channelName);
            if (channelId != null)
                return channelId;

            channelId = await youtubeClient.GetChannelIdFromChannelName(channelName, cancellationToken);
            // the id will never change so cache it forever once it's found
            cache.Add(channelName, channelId, DateTimeOffset.MaxValue);
            return channelId;
        }

        private async Task<List<LivestreamQueryResult>> QueryChannels(
            IReadOnlyCollection<ChannelIdentifier> identifiers,
            CancellationToken cancellationToken)
        {
            var list = await identifiers.ExecuteInParallel(async channelIdentifier =>
            {
                var queryResults = new List<LivestreamQueryResult>();

                try
                {
                    var channelId = await GetChannelIdByChannelName(channelIdentifier.ChannelId, cancellationToken);
                    var videoIds = await GetVideoIdsByChannelId(channelId, cancellationToken);
                    var livestreamModels = await GetLivestreamModels(channelIdentifier, videoIds, cancellationToken);
                    queryResults.AddRange(livestreamModels.Select(x => new LivestreamQueryResult(channelIdentifier)
                    {
                        LivestreamModel = x,
                    }));

                    // video ids are only returned for online streams, we need to show something to the user
                    // so create a placeholder livestream model object for the offline channel.
                    // TODO - query the channel to get their display name information rather than using the actual channel name
                    if (!videoIds.Any())
                    {
                        queryResults.Add(new LivestreamQueryResult(channelIdentifier)
                        {
                            LivestreamModel = new LivestreamModel("offline", channelIdentifier)
                            {
                                DisplayName = channelIdentifier.ChannelId,
                                Description = "[Livestream Monitor - offline youtube stream]",
                            }
                        });
                    }
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

        private async Task<List<string>> GetVideoIdsByChannelId(string channelId, CancellationToken cancellationToken)
        {
            var searchLiveVideosRoot = await youtubeClient.GetLivestreamVideos(channelId, cancellationToken);
            var videoIds = searchLiveVideosRoot.Items?.Select(x => x.Id.VideoId).ToList();

            if (videoIds == null)
                return new List<string>();

            return videoIds;
        }

        private async Task<List<LivestreamModel>> GetLivestreamModels(ChannelIdentifier channelIdentifier, List<string> videoIds, CancellationToken cancellationToken)
        {
            var livestreamModels = new List<LivestreamModel>();

            foreach (var videoId in videoIds)
            {
                LiveStreamingDetails livestreamDetails = null;
                VideoRoot videoRoot = null;

                int retryCount = 0;
                while (retryCount < 3 && livestreamDetails == null)
                {
                    if (cancellationToken.IsCancellationRequested) return livestreamModels;

                    try
                    {
                        videoRoot = await youtubeClient.GetLivestreamDetails(videoId, cancellationToken);
                        livestreamDetails = videoRoot.Items?.FirstOrDefault()?.LiveStreamingDetails;
                    }
                    catch (HttpRequestWithStatusException ex) when (ex.StatusCode == HttpStatusCode.ServiceUnavailable)
                    {
                        await Task.Delay(2000, cancellationToken);
                    }
                    retryCount++;
                }
                
                if (livestreamDetails == null) continue;

                var snippet = videoRoot.Items?.FirstOrDefault()?.Snippet;
                if (snippet == null) continue;
                
                var livestreamModel = new LivestreamModel(videoId, channelIdentifier) { Live = snippet.LiveBroadcastContent != "none" };
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