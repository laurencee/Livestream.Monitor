using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExternalAPIs.TwitchTv;
using ExternalAPIs.TwitchTv.Query;
using Livestream.Monitor.Core;

namespace Livestream.Monitor.Model.ApiClients
{
    public class TwitchApiClient : IApiClient
    {
        public const string API_NAME = "twitchtv";
        public const string BroadcastVodType = "Broadcasts";
        public const string HighlightVodType = "Highlights";

        private readonly ITwitchTvReadonlyClient twitchTvClient;

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

        public async Task<List<LivestreamModel>> UpdateOnlineStreams(List<LivestreamModel> livestreams, CancellationToken cancellationToken)
        {
            var onlineStreams = await twitchTvClient.GetStreamsDetails(livestreams.Select(x => x.Id));

            foreach (var onlineStream in onlineStreams)
            {
                if (cancellationToken.IsCancellationRequested) return new List<LivestreamModel>();

                var livestream = livestreams.Single(x => x.Id.IsEqualTo(onlineStream.Channel.Name));

                livestream.PopulateWithChannel(onlineStream.Channel);
                livestream.PopulateWithStreamDetails(onlineStream, this);
            }

            return livestreams.Where(x => !onlineStreams.Any(y => y.Channel.Name.IsEqualTo(x.Id))).ToList();
        }

        public async Task UpdateOfflineStreams(List<LivestreamModel> livestreams, CancellationToken cancellationToken)
        {
            var offlineTasks = livestreams.Select(x => new
            {
                Livestream = x,
                OfflineData = twitchTvClient.GetChannelDetails(x.Id)
            }).ToList();

            await Task.WhenAll(offlineTasks.Select(x => x.OfflineData));
            foreach (var offlineTask in offlineTasks)
            {
                if (cancellationToken.IsCancellationRequested) return;

                var offlineData = offlineTask.OfflineData.Result;
                if (offlineData == null) continue;

                offlineTask.Livestream.PopulateWithChannel(offlineData);
            }
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

        public async Task<List<LivestreamModel>> GetTopStreams(TopStreamQuery topStreamQuery)
        {
            if (topStreamQuery == null) throw new ArgumentNullException(nameof(topStreamQuery));
            var topStreams = await twitchTvClient.GetTopStreams(topStreamQuery);

            return topStreams.Select(x =>
            {
                var livestreamModel = new LivestreamModel();
                livestreamModel.PopulateWithStreamDetails(x, this);
                livestreamModel.PopulateWithChannel(x.Channel);
                return livestreamModel;
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

        public async Task<List<LivestreamModel>> GetUserFollows(string userName)
        {
            var userFollows = await twitchTvClient.GetUserFollows(userName);
            return (from follow in userFollows.Follows
                    select new LivestreamModel()
                    {
                        Id = follow.Channel?.Name,
                        ApiClient = this,
                        DisplayName = follow.Channel?.Name,
                        Description = follow.Channel?.Status,
                        Game = follow.Channel?.Game,
                        IsPartner = follow.Channel?.Partner != null && follow.Channel.Partner.Value,
                        ImportedBy = userName,
                        BroadcasterLanguage = follow.Channel?.BroadcasterLanguage
                    }).ToList();
        }
    }
}