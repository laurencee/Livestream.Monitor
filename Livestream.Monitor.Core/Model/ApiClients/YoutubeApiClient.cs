using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using ExternalAPIs;
using ExternalAPIs.Youtube;
using ExternalAPIs.Youtube.Dto;
using ExternalAPIs.Youtube.Query;
using Livestream.Monitor.Core;
using Livestream.Monitor.Model.Monitoring;

namespace Livestream.Monitor.Model.ApiClients
{
    public class YoutubeApiClient(IYoutubeReadonlyClient youtubeClient, ICacheHandler cacheHandler) : IApiClient
    {
        public const string API_NAME = "youtube";

        // Keep the initial playlist request small to reduce startup delay.
        private const int QuickCheckCandidates = 5;
        // Older entries may still be live, but scanning more than one page can exceed the refresh timeout.
        private const int CompleteCheckCandidates = 50;

        private readonly IYoutubeReadonlyClient youtubeClient = youtubeClient ?? throw new ArgumentNullException(nameof(youtubeClient));
        private readonly ICacheHandler cacheHandler = cacheHandler ?? throw new ArgumentNullException(nameof(cacheHandler));
        private readonly HashSet<ChannelIdentifier> monitoredChannels = new();
        private readonly Dictionary<VodQuery, string> vodsPageTokenMap = new();
        private readonly Dictionary<string, KnownVideosLookup> knownVideosLookups = new();

        public string ApiName => API_NAME;

        public string BaseUrl => @"https://www.youtube.com/";

        public bool HasChatSupport => true;

        public bool HasVodViewerSupport => true;

        public bool HasTopStreamsSupport => false;

        public bool HasTopStreamGameFilterSupport => false;

        public bool HasFollowedChannelsQuerySupport => false;

        public bool IsAuthorized => true;
        public bool CanAuthorizeWithoutUserInput => true;

        public bool HasQuickQuerySupport => true;

        public List<string> VodTypes { get; } = new();

        public Task Authorize(IUserDialogs screen) => Task.FromResult(true);

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
            var queryResults = await QueryChannels([newChannel], complete: true, forgetOtherChannels: false);
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
            return QueryChannels(monitoredChannels, complete: true, forgetOtherChannels: true, cancellationToken);
        }

        /// <summary>
        /// Checks known video IDs, or a few recent playlist entries if the channel has no cache entry.
        /// Channels with cached IDs need a complete check to discover newer livestreams.
        /// </summary>
        public Task<List<LivestreamQueryResult>> QueryChannelsQuickly(CancellationToken cancellationToken)
        {
            return QueryChannels(monitoredChannels, complete: false, forgetOtherChannels: false, cancellationToken);
        }

        public async Task<IReadOnlyCollection<VodDetails>> GetVods(VodQuery vodQuery)
        {
            ChannelsRoot channelsRoot;
            var channelIdentifier = monitoredChannels.FirstOrDefault(x =>
                x.ChannelId == vodQuery.StreamDisplayName || x.DisplayName.IsEqualTo(vodQuery.StreamDisplayName));
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
                var duration = XmlConvert.ToTimeSpan(x.ContentDetails.Duration); // didn't wanna bother writing my own parser

                var vodDetails = new VodDetails()
                {
                    Url = "https://www.youtube.com/watch?v=" + x.Id,
                    Length = duration,
                    Views = x.Statistics.ViewCount,
                    Description = VodDetails.NormalizeDescription(x.Snippet.Description),
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
            bool complete,
            bool forgetOtherChannels,
            CancellationToken cancellationToken = default)
        {
            var knownVideoIds = cacheHandler.Cache.YouTubeLivestreamVideoIds;
            var tasks = identifiers.Select(async channelIdentifier =>
            {
                var queryResults = new List<LivestreamQueryResult>();
                List<string> playlistVideoIds = null;

                try
                {
                    // Ignore cache entries that exceed the video lookup limit.
                    var checkedBefore = knownVideoIds.TryGetValue(channelIdentifier.ChannelId, out var known) &&
                                        known.Count <= CompleteCheckCandidates;
                    if (!checkedBefore) known = [];

                    // Quick checks skip the playlist for channels with known IDs; a livestream started since waits for the next complete check.
                    var candidates = complete ? CompleteCheckCandidates : checkedBefore ? 0 : QuickCheckCandidates;
                    var videos = await GetLivestreamVideos(channelIdentifier.ChannelId, candidates, known, cancellationToken);
                    playlistVideoIds = videos.PlaylistVideoIds;

                    // Complete checks report only broadcasts on the playlist page they read.
                    // Quick checks read too little of the playlist to rule out known videos.
                    var liveVideos = complete ? videos.LiveVideos.Where(x => playlistVideoIds.Contains(x.Id)).ToList() : videos.LiveVideos;
                    var livestreamModels = MapLivestreamModels(channelIdentifier, liveVideos);

                    // Keep an offline placeholder when the playlist contains no active broadcasts.
                    if (livestreamModels.Count == 0)
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
                        return new ChannelCheck(channelIdentifier.ChannelId, queryResults, playlistVideoIds);
                    }

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

                return new ChannelCheck(channelIdentifier.ChannelId, queryResults, playlistVideoIds);
            });
            var checks = await Task.WhenAll(tasks);

            if (complete) cacheHandler.Update(KeepPlaylistEntries);

            return checks.SelectMany(x => x.QueryResults).ToList();

            // Keep the previous IDs for channels whose check failed.
            Cache KeepPlaylistEntries(Cache cache)
            {
                var previous = cache.YouTubeLivestreamVideoIds;
                var checkedChannelIds = checks.Select(x => x.ChannelId).ToHashSet();
                var updated = new Dictionary<string, IReadOnlyList<string>>();

                // Don't remove IDs replaced by a concurrent check, e.g. while adding a channel.
                foreach (var (channelId, ids) in previous)
                {
                    var forget = forgetOtherChannels &&
                                 !checkedChannelIds.Contains(channelId) &&
                                 knownVideoIds.TryGetValue(channelId, out var idsAtStart) &&
                                 ReferenceEquals(idsAtStart, ids);
                    if (!forget) updated[channelId] = ids;
                }

                foreach (var check in checks)
                    if (check.PlaylistVideoIds != null)
                        updated[check.ChannelId] = check.PlaylistVideoIds;

                // Publish new list instances for individual channel checks, even when the IDs haven't changed.
                // This stops a concurrent full check from removing them as stale entries.
                var unchanged = forgetOtherChannels &&
                                updated.Count == previous.Count &&
                                updated.All(x => previous.TryGetValue(x.Key, out var ids) && ids.SequenceEqual(x.Value));

                return unchanged ? cache : cache with { YouTubeLivestreamVideoIds = updated };
            }
        }

        /// <param name="PlaylistVideoIds"> Null when the check failed. </param>
        private sealed record ChannelCheck(string ChannelId, List<LivestreamQueryResult> QueryResults, List<string> PlaylistVideoIds);

        /// <param name="LiveVideos"> Live videos from both the playlist and known IDs. </param>
        /// <param name="PlaylistVideoIds"> IDs from the playlist only, newest first. </param>
        private sealed record LivestreamVideos(List<Video> LiveVideos, List<string> PlaylistVideoIds);

        private sealed record KnownVideosLookup(IReadOnlyList<string> VideoIds, Lazy<Task<List<Video>>> LiveVideos)
        {
            public bool IsCompleted => LiveVideos.IsValueCreated && LiveVideos.Value.IsCompleted;
        }

        /// <summary>
        /// Finds live broadcasts among recent playlist entries and known video IDs.
        /// <paramref name="candidates"/> is the number of playlist entries to read; zero skips the playlist request.
        /// <paramref name="knownVideoIds"/> contains up to 50 video IDs to check alongside the playlist.
        /// </summary>
        private async Task<LivestreamVideos> GetLivestreamVideos(
            string channelId,
            int candidates,
            IReadOnlyList<string> knownVideoIds,
            CancellationToken cancellationToken)
        {
            const int MaxAttempts = 3;
            const int RetryDelayMilliseconds = 2000;
            LivestreamVideos videos = null;
            var retryCount = 0;

            while (retryCount < MaxAttempts && videos == null)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    videos = await ReadLivestreamVideos();
                }
                catch (HttpRequestWithStatusException ex) when (ex.StatusCode == HttpStatusCode.ServiceUnavailable)
                {
                    await Task.Delay(RetryDelayMilliseconds, cancellationToken);
                }
                catch (HttpRequestWithStatusException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                {
                    // Channels with no livestream playlist are offline.
                    return new LivestreamVideos([], []);
                }

                retryCount++;
            }

            if (videos == null)
                throw new HttpRequestWithStatusException(HttpStatusCode.ServiceUnavailable, "YouTube remained unavailable after retries.");

            return videos;

            async Task<LivestreamVideos> ReadLivestreamVideos()
            {
                // Start the known-video lookup without waiting for the playlist.
                var knownLookup = LookUpKnownVideos(channelId, knownVideoIds, cancellationToken);
                if (candidates == 0) return new LivestreamVideos([.. await knownLookup], []);

                var playlistRequest = youtubeClient.GetLivestreamPlaylistVideoIds(channelId, candidates, cancellationToken);
                await Task.WhenAll(knownLookup, playlistRequest);

                // Copy before adding videos; another check may be using the same result list.
                List<Video> liveVideos = [.. await knownLookup];
                var playlistVideoIds = await playlistRequest;
                var unknownVideoIds = playlistVideoIds.Except(knownVideoIds).ToList();
                if (unknownVideoIds.Count > 0) liveVideos.AddRange(await youtubeClient.GetLiveVideos(unknownVideoIds, cancellationToken));

                return new LivestreamVideos(liveVideos, playlistVideoIds);
            }
        }

        /// <summary>
        /// Shares an in-flight lookup for the same known ID list instance.
        /// The first caller's token cancels the request; each caller's token cancels its own wait.
        /// </summary>
        private Task<List<Video>> LookUpKnownVideos(string channelId, IReadOnlyList<string> knownVideoIds, CancellationToken cancellationToken)
        {
            if (knownVideoIds.Count == 0) return Task.FromResult(new List<Video>());

            // Lazy starts the shared request once, outside the lock.
            // The async wrapper turns synchronous failures into faulted tasks so later checks can replace the failed lookup.
            KnownVideosLookup lookup;
            lock (knownVideosLookups)
            {
                var shared = knownVideosLookups.TryGetValue(channelId, out lookup) &&
                             ReferenceEquals(lookup.VideoIds, knownVideoIds) &&
                             !lookup.IsCompleted;
                if (!shared)
                {
                    var liveVideos = new Lazy<Task<List<Video>>>(async () => await youtubeClient.GetLiveVideos(knownVideoIds, cancellationToken));
                    lookup = new KnownVideosLookup(knownVideoIds, liveVideos);
                    knownVideosLookups[channelId] = lookup;
                }
            }

            return lookup.LiveVideos.Value.WaitAsync(cancellationToken);
        }

        private List<LivestreamModel> MapLivestreamModels(ChannelIdentifier channelIdentifier, List<Video> videos)
        {
            var livestreamModels = new List<LivestreamModel>();
            if (videos == null) return livestreamModels;

            foreach (var videoDetails in videos)
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
