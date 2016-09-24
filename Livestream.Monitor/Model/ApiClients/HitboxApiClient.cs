using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using ExternalAPIs;
using ExternalAPIs.Hitbox;
using ExternalAPIs.TwitchTv.Query;
using Livestream.Monitor.Core;
using Livestream.Monitor.Model.Monitoring;
using static System.String;

namespace Livestream.Monitor.Model.ApiClients
{
    public class HitboxApiClient : IApiClient
    {
        public const string API_NAME = "hitbox";
        // this doesn't appear anywhere in the returned api values but it is where the static data comes from...
        private const string StaticContentPrefixUrl = "http://edge.sf.hitbox.tv";
        private const string VideoPrefix = "http://www.hitbox.tv/video/";

        private readonly IHitboxReadonlyClient hitboxClient;
        private readonly HashSet<ChannelIdentifier> moniteredChannels = new HashSet<ChannelIdentifier>();

        public HitboxApiClient(IHitboxReadonlyClient hitboxClient)
        {
            if (hitboxClient == null) throw new ArgumentNullException(nameof(hitboxClient));
            this.hitboxClient = hitboxClient;
        }

        public string ApiName => API_NAME;

        public string BaseUrl => "https://www.hitbox.tv/";

        public bool HasChatSupport => true;

        public bool HasVodViewerSupport => true;

        public bool HasTopStreamsSupport => true;

        public bool HasTopStreamGameFilterSupport => true;

        public bool HasUserFollowQuerySupport => true;

        public List<string> VodTypes { get; } = new List<string>();

        public string GetStreamUrl(string channelId)
        {
            if (IsNullOrWhiteSpace(channelId)) throw new ArgumentException("Argument is null or whitespace", nameof(channelId));

            return $"{BaseUrl}{channelId}";
        }

        public string GetChatUrl(string channelId)
        {
            if (IsNullOrWhiteSpace(channelId)) throw new ArgumentException("Argument is null or whitespace", nameof(channelId));

            return $"{BaseUrl}embedchat/{channelId}?autoconnect=true";
        }

        public async Task<List<LivestreamQueryResult>> AddChannel(ChannelIdentifier newChannel)
        {
            if (newChannel == null) throw new ArgumentNullException(nameof(newChannel));

            var queryResults = await QueryChannels(new[] { newChannel }, CancellationToken.None);
            if (queryResults.Any(x => x.IsSuccess))
                moniteredChannels.Add(newChannel);

            return queryResults;
        }

        public void AddChannelWithoutQuerying(ChannelIdentifier newChannel)
        {
            if (newChannel == null) throw new ArgumentNullException(nameof(newChannel));
            moniteredChannels.Add(newChannel);
        }

        public Task RemoveChannel(ChannelIdentifier channelIdentifier)
        {
            moniteredChannels.Remove(channelIdentifier);
            return Task.CompletedTask;
        }

        public Task<List<LivestreamQueryResult>> QueryChannels(CancellationToken cancellationToken)
        {
            return QueryChannels(moniteredChannels, cancellationToken);
        }

        public async Task<List<VodDetails>> GetVods(VodQuery vodQuery)
        {
            var channelVideosQuery = new ExternalAPIs.Hitbox.Query.ChannelVideosQuery(vodQuery.StreamId);
            try
            {
                var videos = await hitboxClient.GetChannelVideos(channelVideosQuery);

                return videos.Select(x =>
                {
                    TimeSpan length;
                    if (!TimeSpan.TryParse(x.MediaDurationFormat, out length))
                        length = TimeSpan.Zero;

                    return new VodDetails()
                    {
                        Length = length,
                        Game = x.CategoryName,
                        Url = VideoPrefix + x.MediaId,
                        Title = x.MediaTitle,
                        Description = x.MediaStatus,
                        RecordedAt = x.MediaDateAdded ?? DateTimeOffset.MinValue,
                        Views = x.MediaViews,
                        PreviewImage = StaticContentPrefixUrl + x.MediaThumbnail
                    };
                }).ToList();
            }
            // special case exception handling for no videos available
            // unfortunately hitbox doesn't differentiate between a valid channel with no videos and an invalid channel
            // an email has been sent so hopefully they'll sort this out in the future and I can remove this comment!
            catch (HttpRequestWithStatusException ex)
                when (ex.StatusCode == HttpStatusCode.NotFound && ex.Message.Contains("no_media_found"))
            {
                return new List<VodDetails>();
            }
        }

        public async Task<List<LivestreamQueryResult>> GetTopStreams(TopStreamQuery topStreamQuery)
        {
            var hitboxTopStreamQuery = new ExternalAPIs.Hitbox.Query.TopStreamsQuery()
            {
                GameName = topStreamQuery.GameName
            };
            var topStreams = await hitboxClient.GetTopStreams(hitboxTopStreamQuery);
            if (topStreams == null) return new List<LivestreamQueryResult>();

            return topStreams.ConvertAll(ConvertToLivestreamModel)
                             .Select(x => new LivestreamQueryResult(new ChannelIdentifier(this, x.Id))
                             {
                                 LivestreamModel = x,
                             }).ToList();
        }

        public async Task<List<KnownGame>> GetKnownGameNames(string filterGameName)
        {
            var topGames = await hitboxClient.GetTopGames(filterGameName);
            return topGames.Select(x => new KnownGame()
            {
                GameName = x.CategoryName,
                TotalViewers = x.CategoryViewers,
                ThumbnailUrls = new ThumbnailUrls()
                {
                    Large = x.CategoryLogoLarge,
                    Medium = x.CategoryLogoSmall,
                    Small = x.CategoryLogoSmall
                }
            }).ToList();
        }

        public async Task<List<LivestreamQueryResult>> GetUserFollows(string userName)
        {
            var userFollows = await hitboxClient.GetUserFollows(userName);
            return userFollows.Select(x =>
            {
                var channelIdentifier = new ChannelIdentifier(this, x.UserName);
                return new LivestreamQueryResult(channelIdentifier)
                {
                    LivestreamModel = new LivestreamModel(x.UserName, channelIdentifier)
                };
            }).ToList();
        }

        private Task<List<LivestreamQueryResult>> QueryChannels(
            IReadOnlyCollection<ChannelIdentifier> identifiers,
            CancellationToken cancellationToken)
        {
            return identifiers.ExecuteInParallel(
                query: async channelIdentifier =>
                {
                    var queryResult = new LivestreamQueryResult(channelIdentifier);
                    try
                    {
                        var livestream = await hitboxClient.GetChannelDetails(channelIdentifier.ChannelId, cancellationToken);
                        queryResult.LivestreamModel = ConvertToLivestreamModel(livestream);
                    }
                    catch (Exception ex)
                    {
                        queryResult.FailedQueryException = new FailedQueryException(channelIdentifier, ex);
                    }
                    return queryResult;
                },
                timeout: Constants.HalfRefreshPollingTime,
                cancellationToken: cancellationToken);
        }

        private LivestreamModel ConvertToLivestreamModel(ExternalAPIs.Hitbox.Dto.Livestream livestream)
        {
            var existingChannel = moniteredChannels.FirstOrDefault(x => x.ChannelId.IsEqualTo(livestream.Channel?.UserName));
            var livestreamModel = new LivestreamModel(livestream.Channel?.UserName, existingChannel ?? new ChannelIdentifier(this, livestream.Channel?.UserName));

            livestreamModel.DisplayName = livestream.MediaDisplayName;
            livestreamModel.Description = livestream.MediaStatus;
            livestreamModel.Game = livestream.CategoryName;
            livestreamModel.BroadcasterLanguage = livestream.MediaCountries?.FirstOrDefault()?.ToLower();
            livestreamModel.ThumbnailUrls = new ThumbnailUrls()
            {
                Medium = StaticContentPrefixUrl + livestream.MediaThumbnail,
                Small = StaticContentPrefixUrl + livestream.MediaThumbnail,
                Large = StaticContentPrefixUrl + livestream.MediaThumbnailLarge
            };

            if (livestream.MediaIsLive)
            {
                livestreamModel.Viewers = livestream.MediaViews;
                livestreamModel.StartTime = livestream.MediaLiveSince ?? DateTimeOffset.Now;
                livestreamModel.Live = true;
            }
            else
                livestreamModel.Offline();

            return livestreamModel;
        }
    }
}
