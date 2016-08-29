using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExternalAPIs.Beam.Pro;
using ExternalAPIs.Beam.Pro.Dto;
using ExternalAPIs.Beam.Pro.Query;
using ExternalAPIs.TwitchTv.Query;
using Livestream.Monitor.Core;
using Livestream.Monitor.Model.Monitoring;

namespace Livestream.Monitor.Model.ApiClients
{
    public class BeamProApiClient : IApiClient
    {
        public const string API_NAME = "beam.pro";

        private readonly IBeamProReadonlyClient beamProClient;
        private readonly Dictionary<string, int> channelNameIdMap = new Dictionary<string, int>();
        private readonly HashSet<ChannelIdentifier> followedChannels = new HashSet<ChannelIdentifier>();

        public BeamProApiClient(IBeamProReadonlyClient beamProClient)
        {
            if (beamProClient == null) throw new ArgumentNullException(nameof(beamProClient));
            this.beamProClient = beamProClient;
        }

        public string ApiName => API_NAME;

        public string BaseUrl => @"https://beam.pro/";

        public bool HasChatSupport => true;

        public bool HasVodViewerSupport => true;

        public bool HasTopStreamsSupport => true;

        public bool HasTopStreamGameFilterSupport => false;

        public bool HasUserFollowQuerySupport => false;

        public List<string> VodTypes { get; }

        public string GetStreamUrl(string channelId)
        {
            return BaseUrl + channelId;
        }

        public string GetChatUrl(string channelId)
        {
            return BaseUrl + "embed/chat/" + channelId;
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
                    var channel = await beamProClient.GetStreamDetails(vodQuery.StreamId, CancellationToken.None);
                    channelNameIdMap[channel.token] = channel.id;
                }
            }

            var pagedQuery = new BeamProPagedQuery() { Skip = vodQuery.Skip, Take = vodQuery.Take };
            var vods = await beamProClient.GetChannelVideos(streamid, pagedQuery);

            var vodDetails = vods.ConvertAll(input => new VodDetails()
            {
                Views = input.viewsTotal.GetValueOrDefault(),
                Length = TimeSpan.FromSeconds(input.duration),
                Title = input.name,
                RecordedAt = input.createdAt.GetValueOrDefault(),
                Url = input.vods?[0]?.baseUrl,
            });

            return vodDetails;
        }

        public async Task<List<LivestreamQueryResult>> GetTopStreams(TopStreamQuery topStreamQuery)
        {
            var pagedQuery = new BeamProPagedQuery()
            {
                Skip = topStreamQuery.Skip,
                Take = topStreamQuery.Take
            };
            var topStreams = await beamProClient.GetTopStreams(pagedQuery);

            return topStreams.ConvertAll(input =>
            {
                var channelIdentifier = new ChannelIdentifier(this, input.token);
                return new LivestreamQueryResult(channelIdentifier)
                {
                    LivestreamModel = ConvertToLivestreamModel(input)
                };
            });
        }

        public async Task<List<KnownGame>> GetKnownGameNames(string filterGameName)
        {
            var games = await beamProClient.GetKnownGames(new KnownGamesPagedQuery() {GameName = filterGameName});
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

        private async Task<LivestreamQueryResult> QueryChannel(ChannelIdentifier channelIdentifier, CancellationToken cancellationToken)
        {
            var queryResult = new LivestreamQueryResult(channelIdentifier);

            try
            {
                var channel = await beamProClient.GetStreamDetails(channelIdentifier.ChannelId, cancellationToken);
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
                DisplayName = channel.name,
                // Beam.Pro doesn't seem to have a short description field like twitch/youtube etc.
                // the "description" field from the channel is the full html output from the channels description, not the current live stream
                //Description = channel.user?.bio,
                Viewers = channel.viewersCurrent,
                Live = channel.online,
                Game = channel.type?.name,
                // unable to find an uptime/starttime value from the api
                ThumbnailUrls = new ThumbnailUrls()
                {
                    Large = channel.thumbnail.url,
                    Medium = channel.thumbnail.url,
                    Small = channel.thumbnail.url
                }
            };
        }
    }
}