using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExternalAPIs.TwitchTv;
using ExternalAPIs.TwitchTv.Query;
using Livestream.Monitor.Core;
using Livestream.Monitor.Model.Monitoring;

namespace Livestream.Monitor.Model.ApiClients
{
    public class TwitchApiClient : IApiClient
    {
        public const string API_NAME = "twitchtv";
        public const string BroadcastVodType = "Broadcasts";
        public const string HighlightVodType = "Highlights";

        private readonly ITwitchTvReadonlyClient twitchTvClient;
        private List<LivestreamQueryResult> lastQueryResults = new List<LivestreamQueryResult>();
        private HashSet<ChannelIdentifier> channelIdentifiers = new HashSet<ChannelIdentifier>();

        // 1 time per application run
        private bool queryOfflineStreams = true;

        public TwitchApiClient(ITwitchTvReadonlyClient twitchTvClient)
        {
            if (twitchTvClient == null) throw new ArgumentNullException(nameof(twitchTvClient));
            this.twitchTvClient = twitchTvClient;
        }

        public string ApiName => API_NAME;

        public string BaseUrl => @"http://www.twitch.tv/";

        public bool HasChatSupport => true;

        public bool HasVodViewerSupport => true;

        public bool HasTopStreamsSupport => true;

        public bool HasUserFollowQuerySupport => true;

        public List<string> VodTypes { get; } = new List<string>()
        {
            BroadcastVodType,
            HighlightVodType
        };

        public string GetStreamUrl(string channelId)
        {
            if (string.IsNullOrWhiteSpace(channelId)) throw new ArgumentNullException(nameof(channelId));

            return $"{BaseUrl}{channelId}/";
        }

        public string GetChatUrl(string channelId)
        {
            if (string.IsNullOrWhiteSpace(channelId)) throw new ArgumentNullException(nameof(channelId));

            return $"{BaseUrl}{channelId}/chat?popout=true";
        }

        public async Task<List<LivestreamQueryResult>> GetLivestreams(List<ChannelIdentifier> channelIdentifiers, CancellationToken cancellationToken)
        {
            var onlineStreams = await twitchTvClient.GetStreamsDetails(channelIdentifiers.Select(x => x.ChannelId));

            var queryResults = new List<LivestreamQueryResult>();
            foreach (var onlineStream in onlineStreams)
            {
                if (cancellationToken.IsCancellationRequested) return queryResults;

                var channelIdentifier = new ChannelIdentifier(this, onlineStream.Channel?.Name);
                var livestream = new LivestreamModel(onlineStream.Channel?.Name, channelIdentifier);
                livestream.PopulateWithChannel(onlineStream.Channel);
                livestream.PopulateWithStreamDetails(onlineStream);
                queryResults.Add(new LivestreamQueryResult(channelIdentifier)
                {
                    LivestreamModel = livestream
                });
            }

            var offlineChannels = channelIdentifiers.Where(x => onlineStreams.All(y => y.Channel.Name != x.ChannelId)).ToList();

            // we only need to query offline streams once to get their initial state
            if (queryOfflineStreams)
            {
                var offlineStreams = await GetOfflineStreamQueryResults(offlineChannels, cancellationToken);
                queryResults.AddRange(offlineStreams);
                queryOfflineStreams = false;
            }

            // cleanup any previous query results that have channel identifiers that no longer exist
            var removedChannelIds = lastQueryResults.Where(x => !channelIdentifiers.Contains(x.ChannelIdentifier));
            lastQueryResults = lastQueryResults.Except(removedChannelIds).ToList();

            foreach (var offlineQueryResult in lastQueryResults.Except(queryResults).Where(x => x.IsSuccess))
            {
                offlineQueryResult.LivestreamModel.Offline();
            }

            queryResults.AddRange(lastQueryResults.Where(x => !x.LivestreamModel.Live));
            lastQueryResults = queryResults;
            return queryResults;
        }

        private async Task<List<LivestreamQueryResult>> GetOfflineStreamQueryResults(
            IEnumerable<ChannelIdentifier> offlineChannels, CancellationToken cancellationToken)
        {
            return await offlineChannels.ExecuteInParallel(async channelIdentifier =>
            {
                var queryResult = new LivestreamQueryResult(channelIdentifier);
                try
                {
                    var channel = await twitchTvClient.GetChannelDetails(channelIdentifier.ChannelId);
                    queryResult.LivestreamModel = new LivestreamModel(channelIdentifier.ChannelId, channelIdentifier);
                    queryResult.LivestreamModel.PopulateWithChannel(channel);
                    queryResult.LivestreamModel.Offline();
                }
                catch (Exception ex)
                {
                    queryResult.FailedQueryException = new FailedQueryException(channelIdentifier, ex);
                }

                return queryResult;
            }, Constants.HalfRefreshPollingTime, cancellationToken);
        }

        public async Task<List<VodDetails>> GetVods(VodQuery vodQuery)
        {
            if (vodQuery == null) throw new ArgumentNullException(nameof(vodQuery));
            if (string.IsNullOrWhiteSpace(vodQuery.StreamId)) throw new ArgumentNullException(nameof(vodQuery.StreamId));

            var channelVideosQuery = new ChannelVideosQuery()
            {
                ChannelName = vodQuery.StreamId,
                Take = vodQuery.Take,
                Skip = vodQuery.Skip,
                HLSVodsOnly = true,
                BroadcastsOnly = vodQuery.VodTypes.Contains(BroadcastVodType)
            };
            var channelVideos = await twitchTvClient.GetChannelVideos(channelVideosQuery);
            var vods = channelVideos.Select(channelVideo => new VodDetails
            {
                Url = channelVideo.Url,
                Length = TimeSpan.FromSeconds(channelVideo.Length),
                RecordedAt = channelVideo.RecordedAt ?? DateTimeOffset.MinValue,
                Views = channelVideo.Views,
                Game = channelVideo.Game,
                Description = channelVideo.Description,
                Title = channelVideo.Title,
                PreviewImage = channelVideo.Preview
            }).ToList();

            return vods;
        }

        public async Task<List<LivestreamQueryResult>> GetTopStreams(TopStreamQuery topStreamQuery)
        {
            if (topStreamQuery == null) throw new ArgumentNullException(nameof(topStreamQuery));
            var topStreams = await twitchTvClient.GetTopStreams(topStreamQuery);

            return topStreams.Select(x =>
            {
                var channelIdentifier = new ChannelIdentifier(this, x.Channel.Name);
                var queryResult = new LivestreamQueryResult(channelIdentifier);
                var livestreamModel = new LivestreamModel(x.Channel?.Name, channelIdentifier);
                livestreamModel.PopulateWithStreamDetails(x);
                livestreamModel.PopulateWithChannel(x.Channel);

                queryResult.LivestreamModel = livestreamModel;
                return queryResult;
            }).ToList();
        }

        public async Task<List<KnownGame>> GetKnownGameNames(string filterGameName)
        {
            var twitchGames = await twitchTvClient.SearchGames(filterGameName);
            return twitchGames.Select(x => new KnownGame()
            {
                GameName = x.Name,
                ThumbnailUrls = new ThumbnailUrls()
                {
                    Medium = x.Logo?.Medium,
                    Small = x.Logo?.Small,
                    Large = x.Logo?.Large
                }
            }).ToList();
        }

        public async Task<List<LivestreamQueryResult>> GetUserFollows(string userName)
        {
            var userFollows = await twitchTvClient.GetUserFollows(userName);
            return (from follow in userFollows.Follows
                    let channelIdentifier = new ChannelIdentifier(this, follow.Channel.Name)
                    select new LivestreamQueryResult(channelIdentifier)
                    {
                        LivestreamModel = new LivestreamModel(follow.Channel?.Name, channelIdentifier)
                        {
                            DisplayName = follow.Channel?.Name,
                            Description = follow.Channel?.Status,
                            Game = follow.Channel?.Game,
                            IsPartner = follow.Channel?.Partner != null && follow.Channel.Partner.Value,
                            ImportedBy = userName,
                            BroadcasterLanguage = follow.Channel?.BroadcasterLanguage
                        }
                    }).ToList();
        }
    }
}