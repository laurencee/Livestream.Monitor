using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Caliburn.Micro;
using ExternalAPIs.Mixer;
using ExternalAPIs.Mixer.Dto;
using ExternalAPIs.Mixer.Query;
using ExternalAPIs.TwitchTv.V3.Query;
using Livestream.Monitor.Core;
using Livestream.Monitor.Model.Monitoring;

namespace Livestream.Monitor.Model.ApiClients
{
    public class MixerApiClient : IApiClient
    {
        public const string API_NAME = "mixer";

        private readonly IMixerReadonlyClient mixerClient;
        private readonly Dictionary<string, int> channelNameIdMap = new Dictionary<string, int>();
        private readonly HashSet<ChannelIdentifier> followedChannels = new HashSet<ChannelIdentifier>();

        public MixerApiClient(IMixerReadonlyClient mixerClient)
        {
            this.mixerClient = mixerClient ?? throw new ArgumentNullException(nameof(mixerClient));
        }

        public string ApiName => API_NAME;

        public string BaseUrl => @"https://mixer.com/";

        public bool HasChatSupport => true;

        public bool HasVodViewerSupport => true;

        public bool HasTopStreamsSupport => true;

        public bool HasTopStreamGameFilterSupport => false;

        public bool HasUserFollowQuerySupport => false;

        public bool IsAuthorized => true;

        public List<string> VodTypes { get; }

        public string LivestreamerAuthorizationArg => null;

        public Task Authorize(IViewAware screen) => Task.FromResult(true);

        public Task<string> GetStreamUrl(string channelId)
        {
            return Task.FromResult(BaseUrl + channelId);
        }

        public Task<string> GetChatUrl(string channelId)
        {
            return Task.FromResult(BaseUrl + "embed/chat/" + channelId);
        }

        public async Task<List<LivestreamQueryResult>> AddChannel(ChannelIdentifier newChannel)
        {
            var queryResult = await QueryChannel(newChannel, CancellationToken.None);
            if (queryResult.IsSuccess) followedChannels.Add(newChannel);

            return new List<LivestreamQueryResult>() { queryResult };
        }

        public void AddChannelWithoutQuerying(ChannelIdentifier newChannel)
        {
            followedChannels.Add(newChannel);
        }

        public Task RemoveChannel(ChannelIdentifier channelIdentifier)
        {
            followedChannels.Remove(channelIdentifier);
            return Task.CompletedTask;
        }

        public async Task<List<LivestreamQueryResult>> QueryChannels(CancellationToken cancellationToken)
        {
            return await followedChannels.ExecuteInParallel(async channelIdentifier =>
            {
                return await QueryChannel(channelIdentifier, cancellationToken);
            }, Constants.HalfRefreshPollingTime, cancellationToken);
        }

        public async Task<List<VodDetails>> GetVods(VodQuery vodQuery)
        {
            if (string.IsNullOrEmpty(vodQuery.StreamId)) throw new ArgumentNullException("vodQuery.StreamId");

            int streamid;
            if (!int.TryParse(vodQuery.StreamId, out streamid))
            {
                if (!channelNameIdMap.TryGetValue(vodQuery.StreamId, out streamid))
                {
                    var channel = await mixerClient.GetStreamDetails(vodQuery.StreamId, CancellationToken.None);
                    channelNameIdMap[channel.token] = channel.id;
                    streamid = channel.id;
                }
            }

            var pagedQuery = new MixerPagedQuery() { Skip = vodQuery.Skip, Take = vodQuery.Take };
            var vods = await mixerClient.GetChannelVideos(streamid, pagedQuery);

            var vodDetails = vods.Select(x => new VodDetails()
            {
                Views = x.viewsTotal.GetValueOrDefault(),
                Length = TimeSpan.FromSeconds(x.duration),
                Title = x.name,
                RecordedAt = x.createdAt.GetValueOrDefault(),
                Url = $"{BaseUrl}{vodQuery.StreamId}?vod={x.id}",
                ApiClient = this,
            }).ToList();

            return vodDetails;
        }

        public async Task<List<LivestreamQueryResult>> GetTopStreams(TopStreamQuery topStreamQuery)
        {
            var pagedQuery = new MixerPagedQuery()
            {
                Skip = topStreamQuery.Skip,
                Take = topStreamQuery.Take
            };
            var topStreams = await mixerClient.GetTopStreams(pagedQuery);

            return topStreams.ConvertAll(input =>
            {
                var channelIdentifier = new ChannelIdentifier(this, input.token);
                channelNameIdMap[input.token] = input.id;
                return new LivestreamQueryResult(channelIdentifier)
                {
                    LivestreamModel = ConvertToLivestreamModel(input)
                };
            });
        }

        public async Task<List<KnownGame>> GetKnownGameNames(string filterGameName)
        {
            var games = await mixerClient.GetKnownGames(new KnownGamesPagedQuery() {GameName = filterGameName});
            return games.ConvertAll(input => new KnownGame()
            {
                GameName = input.name,
                TotalViewers = input.viewersCurrent,
                ThumbnailUrls = new ThumbnailUrls()
                {
                    Large = input.coverUrl,
                    Medium = input.coverUrl,
                    Small = input.coverUrl
                }
            });
        }

        public Task<List<LivestreamQueryResult>> GetUserFollows(string userName)
        {
            throw new System.NotImplementedException();
        }

        public Task Initialize(CancellationToken cancellationToken = default) => Task.CompletedTask;

        private async Task<LivestreamQueryResult> QueryChannel(ChannelIdentifier channelIdentifier, CancellationToken cancellationToken)
        {
            var queryResult = new LivestreamQueryResult(channelIdentifier);

            try
            {
                var channel = await mixerClient.GetStreamDetails(channelIdentifier.ChannelId, cancellationToken);
                channelNameIdMap[channel.token] = channel.id; // record the mapping of the channels token/name to its underlying id
                var livestreamModel = ConvertToLivestreamModel(channel);
                queryResult.LivestreamModel = livestreamModel;
            }
            catch (Exception ex)
            {
                queryResult.FailedQueryException = new FailedQueryException(channelIdentifier, ex);
            }

            return queryResult;
        }

        private LivestreamModel ConvertToLivestreamModel(Channel channel)
        {
            return new LivestreamModel(channel.token, new ChannelIdentifier(this, channel.token))
            {
                DisplayName = channel.user?.username,
                Description = channel.name?.Trim(),
                Viewers = channel.viewersCurrent,
                Live = channel.online,
                Game = channel.type?.name,
                // unable to find an uptime/starttime value from the api
                ThumbnailUrls = new ThumbnailUrls()
                {
                    Large = channel.thumbnail?.url,
                    Medium = channel.thumbnail?.url,
                    Small = channel.thumbnail?.url
                }
            };
        }
    }
}