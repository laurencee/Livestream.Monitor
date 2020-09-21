using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Caliburn.Micro;
using ExternalAPIs;
using ExternalAPIs.Smashcast;
using ExternalAPIs.Smashcast.Query;
using Livestream.Monitor.Core;
using Livestream.Monitor.Model.Monitoring;
using static System.String;
using ChannelVideosQuery = ExternalAPIs.Smashcast.Query.ChannelVideosQuery;

namespace Livestream.Monitor.Model.ApiClients
{
    public class SmashcastApiClient : IApiClient
    {
        public const string API_NAME = "smashcast";
        // this doesn't appear anywhere in the returned api values but it is where the static data comes from...
        private const string StaticContentPrefixUrl = "https://edge.sf.hitbox.tv";
        private const string VideoPrefix = "https://www.smashcast.tv/video/";

        private readonly ISmashcastReadonlyClient smashcastClient;
        private readonly HashSet<ChannelIdentifier> moniteredChannels = new HashSet<ChannelIdentifier>();

        public SmashcastApiClient(ISmashcastReadonlyClient smashcastClient)
        {
            this.smashcastClient = smashcastClient ?? throw new ArgumentNullException(nameof(smashcastClient));
        }

        public string ApiName => API_NAME;

        public string BaseUrl => "https://www.smashcast.tv/";

        public bool HasChatSupport => true;

        public bool HasVodViewerSupport => true;

        public bool HasTopStreamsSupport => true;

        public bool HasTopStreamGameFilterSupport => true;

        public bool HasUserFollowQuerySupport => true;

        public bool IsAuthorized => true;

        public List<string> VodTypes { get; } = new List<string>();

        public string LivestreamerAuthorizationArg => null;

        public Task Authorize(IViewAware screen) => Task.FromResult(true);

        public Task<string> GetStreamUrl(string channelId)
        {
            if (IsNullOrWhiteSpace(channelId)) throw new ArgumentException("Argument is null or whitespace", nameof(channelId));

            return Task.FromResult($"{BaseUrl}{channelId}");
        }

        public Task<string> GetChatUrl(string channelId)
        {
            if (IsNullOrWhiteSpace(channelId)) throw new ArgumentException("Argument is null or whitespace", nameof(channelId));

            return Task.FromResult($"{BaseUrl}embedchat/{channelId}?autoconnect=true");
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
            var channelVideosQuery = new ChannelVideosQuery(vodQuery.StreamId);
            try
            {
                var videos = await smashcastClient.GetChannelVideos(channelVideosQuery);

                return videos.Select(x =>
                {
                    TimeSpan length;
                    if (!TimeSpan.TryParse(x.MediaDurationFormat, out length))
                        length = TimeSpan.Zero;

                    var singleLineTitle = x.MediaTitle.TrimEnd().Replace('\n', ' ');
                    var singleLineDesc = x.MediaStatus.TrimEnd().Replace('\n', ' ');
                    return new VodDetails()
                    {
                        Length = length,
                        Game = x.CategoryName,
                        Url = VideoPrefix + x.MediaId,
                        Title = singleLineTitle,
                        Description = singleLineDesc,
                        RecordedAt = x.MediaDateAdded ?? DateTimeOffset.MinValue,
                        Views = x.MediaViews,
                        PreviewImage = StaticContentPrefixUrl + x.MediaThumbnailLarge,
                        TileImage = StaticContentPrefixUrl + x.MediaThumbnail,
                        ApiClient = this,
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
            var topStreamsQuery = new TopStreamsQuery()
            {
                GameName = topStreamQuery.GameName
            };

            try
            {
                var topStreams = await smashcastClient.GetTopStreams(topStreamsQuery);
                if (topStreams == null) return new List<LivestreamQueryResult>();

                return topStreams.ConvertAll(ConvertToLivestreamModel)
                                 .Select(x => new LivestreamQueryResult(new ChannelIdentifier(this, x.Id))
                                 {
                                     LivestreamModel = x,
                                 }).ToList();
            }
            catch (HttpRequestWithStatusException ex)
                when (ex.StatusCode == HttpStatusCode.NotFound && ex.Message.Contains("no_media_found"))
            {
                return new List<LivestreamQueryResult>();
            }          
        }

        public async Task<List<KnownGame>> GetKnownGameNames(string filterGameName)
        {
            var topGames = await smashcastClient.GetTopGames(filterGameName);
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
            var userFollows = await smashcastClient.GetUserFollows(userName);
            return userFollows.Select(x =>
            {
                var channelIdentifier = new ChannelIdentifier(this, x.UserName);
                return new LivestreamQueryResult(channelIdentifier)
                {
                    LivestreamModel = new LivestreamModel(x.UserName, channelIdentifier)
                };
            }).ToList();
        }

        public Task Initialize(CancellationToken cancellationToken = default) => Task.CompletedTask;

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
                        var livestream = await smashcastClient.GetChannelDetails(channelIdentifier.ChannelId, cancellationToken);
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

        private LivestreamModel ConvertToLivestreamModel(ExternalAPIs.Smashcast.Dto.Livestream livestream)
        {
            var existingChannel = moniteredChannels.FirstOrDefault(x => x.ChannelId.IsEqualTo(livestream.Channel?.UserName));
            var livestreamModel = new LivestreamModel(livestream.Channel?.UserName, existingChannel ?? new ChannelIdentifier(this, livestream.Channel?.UserName));

            livestreamModel.DisplayName = livestream.MediaDisplayName;
            livestreamModel.Description = livestream.MediaStatus?.Trim();
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
