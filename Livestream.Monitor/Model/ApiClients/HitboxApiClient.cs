using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using ExternalAPIs;
using ExternalAPIs.Hitbox;
using ExternalAPIs.Hitbox.Query;
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

        private readonly IHitboxReadonlyClient hitboxClient;

        public HitboxApiClient(IHitboxReadonlyClient hitboxClient)
        {
            if (hitboxClient == null) throw new ArgumentNullException(nameof(hitboxClient));
            this.hitboxClient = hitboxClient;
        }

        public string ApiName => API_NAME;

        public string BaseUrl => "http://www.hitbox.tv/";

        public bool HasChatSupport => true;

        public bool HasVodViewerSupport => true;

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

        public async Task<List<LivestreamModel>> UpdateOnlineStreams(List<LivestreamModel> livestreams, CancellationToken cancellationToken)
        {
            await livestreams.ExecuteInParallel(
                query: livestreamModel => hitboxClient.GetChannelDetails(livestreamModel.Id),
                postQueryAction: (livestreamModel, livestream) =>
                {
                    livestreamModel.DisplayName = livestream.MediaDisplayName;
                    livestreamModel.Description = livestream.MediaStatus;
                    livestreamModel.Game = livestream.CategoryName;
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
                },
                timeout: MonitorStreamsModel.HalfRefreshPollingTime,
                cancellationToken: cancellationToken);

            return new List<LivestreamModel>();
        }

        public Task UpdateOfflineStreams(List<LivestreamModel> livestreams, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public async Task<List<VodDetails>> GetVods(VodQuery vodQuery)
        {
            var channelVideosQuery = new ChannelVideosQuery(vodQuery.StreamId);
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
                        Url = x.MediaFile,
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
    }
}