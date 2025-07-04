﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Caliburn.Micro;
using ExternalAPIs;
using ExternalAPIs.Youtube;
using ExternalAPIs.Youtube.Dto;
using ExternalAPIs.Youtube.Query;
using Livestream.Monitor.Core;
using Livestream.Monitor.Model.Monitoring;

namespace Livestream.Monitor.Model.ApiClients
{
    public class YoutubeApiClient : IApiClient
    {
        public const string API_NAME = "youtube";

        private readonly IYoutubeReadonlyClient youtubeClient;
        private readonly HashSet<ChannelIdentifier> monitoredChannels = new HashSet<ChannelIdentifier>();
        private readonly Dictionary<VodQuery, string> vodsPageTokenMap = new Dictionary<VodQuery, string>();

        public YoutubeApiClient(IYoutubeReadonlyClient youtubeClient)
        {
            this.youtubeClient = youtubeClient ?? throw new ArgumentNullException(nameof(youtubeClient));
        }

        public string ApiName => API_NAME;

        public string BaseUrl => @"https://www.youtube.com/";

        public bool HasChatSupport => true;

        public bool HasVodViewerSupport => true;

        public bool HasTopStreamsSupport => false;

        public bool HasTopStreamGameFilterSupport => false;

        public bool HasFollowedChannelsQuerySupport => false;

        public bool IsAuthorized => true;

        public List<string> VodTypes { get; } = new List<string>();

        public Task Authorize(IViewAware screen) => Task.FromResult(true);

        public Task<string> GetStreamUrl(LivestreamModel livestreamModel)
        {
            if (livestreamModel == null) throw new ArgumentNullException(nameof(livestreamModel));

            var streamUrl = livestreamModel.Live
                ? $"{BaseUrl}watch?v={livestreamModel.Id}"
                : $"{BaseUrl}{livestreamModel.ChannelIdentifier.DisplayName}"; // use the youtube handle
            return Task.FromResult(streamUrl);
        }

        public Task<string> GetChatUrl(LivestreamModel livestreamModel)
        {
            if (livestreamModel == null) throw new ArgumentNullException(nameof(livestreamModel));

            // the '&from_gaming=1' prevents the annoying popup message appearing at the top of the chat window
            // not all youtube streams have chat support as chat can be disabled for a stream, need to see if there's an api call that provides that info
            return Task.FromResult($"{BaseUrl}live_chat?v={livestreamModel.Id}&dark_theme=1&is_popout=1&from_gaming=1");
        }

        public async Task<List<LivestreamQueryResult>> AddChannel(ChannelIdentifier newChannel)
        {
            var channelId = await GetChannelIdByHandle(newChannel.ChannelId);
            newChannel.DisplayName = newChannel.ChannelId.StartsWith("@") ? newChannel.ChannelId : $"@{newChannel.ChannelId}";
            newChannel.OverrideChannelId(channelId.ChannelId);
            monitoredChannels.Add(newChannel); // if we got this far it means it was a real channel

            // immediately query for livestreams
            var queryResults = await QueryChannels(new[] { newChannel });
            return queryResults;
        }

        public void AddChannelWithoutQuerying(ChannelIdentifier newChannel)
        {
            monitoredChannels.Add(newChannel);
        }

        public Task RemoveChannel(ChannelIdentifier channelIdentifier)
        {
            monitoredChannels.Remove(channelIdentifier);
            return Task.CompletedTask;
        }

        public Task<List<LivestreamQueryResult>> QueryChannels(CancellationToken cancellationToken)
        {
            return QueryChannels(monitoredChannels, cancellationToken);
        }

        public async Task<IReadOnlyCollection<VodDetails>> GetVods(VodQuery vodQuery)
        {
            ChannelsRoot channelsRoot;
            var channelIdentifier = monitoredChannels.FirstOrDefault(x => x.DisplayName.IsEqualTo(vodQuery.StreamDisplayName));
            if (channelIdentifier != null)
            {
                channelsRoot = await youtubeClient.GetChannelDetailsFromIds([channelIdentifier.ChannelId]);
            }
            else
            {
                channelsRoot = await youtubeClient.GetChannelDetailsFromHandle(vodQuery.StreamDisplayName);
            }

            if (channelsRoot?.Items?.Count == 0) return Array.Empty<VodDetails>();

            var channel = channelsRoot!.Items![0];
            var uploadPlaylistId = channel.ContentDetails.RelatedPlaylists.Uploads;

            vodsPageTokenMap.TryGetValue(vodQuery, out var pageToken);
            var query = new PlaylistItemsQuery(uploadPlaylistId)
            {
                PageToken = pageToken,
                ItemsPerPage = vodQuery.Take,
            };
            var playlistItemsRoot = await youtubeClient.GetPlaylistItems(query);

            var nextPageKeyLookup = new VodQuery()
            {
                StreamDisplayName = vodQuery.StreamDisplayName,
                Skip = vodQuery.Skip + vodQuery.Take,
                Take = vodQuery.Take,
                VodTypes = vodQuery.VodTypes,
            };

            if (playlistItemsRoot.NextPageToken != null)
                vodsPageTokenMap[nextPageKeyLookup] = playlistItemsRoot.NextPageToken;

            var videoIds = playlistItemsRoot.Items.Select(x => x.ContentDetails.VideoId).ToArray();
            var videoDetails = await youtubeClient.GetVideosDetails(videoIds);

            var vods = videoDetails.Items.Select(x =>
            {
                var singleLineTitle = x.Snippet.Title.Replace("\r\n", " ").Replace('\n', ' ').TrimEnd();
                var singleLineDescription = x.Snippet.Description.Replace("\r\n", " ").Replace('\n', ' ').TrimEnd();
                var duration = XmlConvert.ToTimeSpan(x.ContentDetails.Duration); // didn't wanna bother writing my own parser

                var vodDetails = new VodDetails()
                {
                    Url = "https://www.youtube.com/watch?v=" + x.Id,
                    Length = duration,
                    Views = x.Statistics.ViewCount,
                    Description = singleLineDescription,
                    Title = singleLineTitle,
                    PreviewImage = x.Snippet.Thumbnails.High.Url,
                    IsUpcoming = x.Snippet.LiveBroadcastContent == "upcoming",
                    ApiClient = this,
                };

                if (x.Snippet.PublishedAt != null)
                    vodDetails.RecordedAt = x.Snippet.PublishedAt.Value;

                return vodDetails;
            }).ToList();

            return vods;
        }

        public Task<TopStreamsResponse> GetTopStreams(TopStreamQuery topStreamQuery)
        {
            throw new NotImplementedException();
        }

        public Task<List<KnownGame>> GetKnownGameNames(string filterGameName)
        {
            throw new NotImplementedException();
        }

        public Task<List<LivestreamQueryResult>> GetFollowedChannels(string userName)
        {
            throw new NotImplementedException();
        }

        public Task<InitializeApiClientResult> Initialize(CancellationToken cancellationToken = default) =>
            Task.FromResult(new InitializeApiClientResult());

        private async Task<ChannelIdentifier> GetChannelIdByHandle(string handle, CancellationToken cancellationToken = default)
        {
            if (handle == null) throw new ArgumentNullException(nameof(handle));

            var channelsRoot = await youtubeClient.GetChannelDetailsFromHandle(handle, cancellationToken);

            var channelDetails = channelsRoot.Items[0];
            return new ChannelIdentifier(this, channelDetails.Id);
        }

        private async Task<List<LivestreamQueryResult>> QueryChannels(
            IReadOnlyCollection<ChannelIdentifier> identifiers,
            CancellationToken cancellationToken = default)
        {
            var list = await identifiers.ExecuteInParallel(async channelIdentifier =>
            {
                var queryResults = new List<LivestreamQueryResult>();

                try
                {
                    var videoIds = await GetVideoIdsByChannelId(channelIdentifier, cancellationToken);

                    // video ids are only returned for online streams, we need to show something to the user
                    // so create a placeholder livestream model object for the offline channel.
                    if (videoIds.Count == 0)
                    {
                        queryResults.Add(new LivestreamQueryResult(channelIdentifier)
                        {
                            LivestreamModel = new LivestreamModel(channelIdentifier.DisplayName, channelIdentifier)
                            {
                                DisplayName = channelIdentifier.DisplayName ?? channelIdentifier.ChannelId,
                                Description = "[Offline youtube stream]",
                                IsChatDisabled = true,
                            },
                        });
                        return queryResults;
                    }

                    var videosRoot = await GetVideosDetails(videoIds, cancellationToken);
                    var livestreamModels = MapLivestreamModels(channelIdentifier, videosRoot);
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

        private async Task<List<string>> GetVideoIdsByChannelId(ChannelIdentifier channelIdentifier, CancellationToken cancellationToken)
        {
            var searchLiveVideosRoot = await youtubeClient.GetLivestreamVideos(channelIdentifier.ChannelId, cancellationToken);
            var videoIds = searchLiveVideosRoot.Items?.Select(x => x.Id.VideoId).ToList();

            return videoIds ?? new List<string>();
        }

        private async Task<VideosRoot> GetVideosDetails(List<string> videoIds, CancellationToken cancellationToken)
        {
            if (videoIds.Count == 0) return null;

            VideosRoot videosRoot = null;
            int retryCount = 0;
            while (retryCount < 3 && videosRoot == null)
            {
                if (cancellationToken.IsCancellationRequested) return null;

                try
                {
                    videosRoot = await youtubeClient.GetVideosDetails(videoIds, cancellationToken);
                }
                catch (HttpRequestWithStatusException ex) when (ex.StatusCode == HttpStatusCode.ServiceUnavailable)
                {
                    await Task.Delay(2000, cancellationToken);
                }
                catch (HttpRequestWithStatusException)
                {
                    // can happen in the case of the video being removed
                    // the youtube api will report the videoid as live but looking up the videoid will fail with BadRequest
                    break;
                }
                retryCount++;
            }

            return videosRoot;
        }

        private List<LivestreamModel> MapLivestreamModels(ChannelIdentifier channelIdentifier, VideosRoot videosRoot)
        {
            var livestreamModels = new List<LivestreamModel>();
            if (videosRoot == null) return livestreamModels;

            foreach (var videoDetails in videosRoot.Items)
            {
                if (videoDetails.LiveStreamingDetails?.ActualStartTime == null) continue;

                var snippet = videoDetails.Snippet;
                if (snippet == null) continue;

                var livestreamModel = new LivestreamModel(videoDetails.Id, channelIdentifier)
                {
                    Live = snippet.LiveBroadcastContent == "live",
                    IsChatDisabled = videoDetails.LiveStreamingDetails.ActiveLiveChatId == null,
                };
                if (!livestreamModel.Live) continue;

                livestreamModel.DisplayName = snippet.ChannelTitle;
                livestreamModel.Description = snippet.Title?.Trim();
                livestreamModel.ThumbnailUrls = new ThumbnailUrls()
                {
                    Small = snippet.Thumbnails?.Default?.Url,
                    Medium = snippet.Thumbnails?.Standard?.Url,
                    Large = snippet.Thumbnails?.Maxres?.Url,
                };

                livestreamModel.Viewers = videoDetails.LiveStreamingDetails.ConcurrentViewers;
                livestreamModel.StartTime = videoDetails.LiveStreamingDetails.ActualStartTime;
                livestreamModels.Add(livestreamModel);
            }

            return livestreamModels;
        }
    }
}