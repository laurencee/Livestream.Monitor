using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.API;
using Livestream.Monitor.Model.Monitoring;

namespace Livestream.Monitor.Model.StreamProviders
{
    public class YoutubeStreamProvider : IStreamProvider
    {
        public const string PROVIDER_NAME = "youtube";

        private readonly IYoutubeReadonlyClient youtubeClient;

        public YoutubeStreamProvider(IYoutubeReadonlyClient youtubeClient)
        {
            if (youtubeClient == null) throw new ArgumentNullException(nameof(youtubeClient));
            this.youtubeClient = youtubeClient;
        }

        public string ProviderName => PROVIDER_NAME;

        public string BaseUrl => @"https://www.youtube.com/";

        public bool HasChatSupport => true;

        public bool HasVodViewerSupport => false;

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

        public async Task<List<LivestreamModel>> UpdateOnlineStreams(List<LivestreamModel> livestreams, CancellationToken cancellationToken)
        {
            var offlineStreams = new List<LivestreamModel>();

            foreach (var livestreamModel in livestreams)
            {
                bool isOffline = true;
                var videoRoot = await youtubeClient.GetLivestreamDetails(livestreamModel.Id);
                if (cancellationToken.IsCancellationRequested) return offlineStreams;

                var snippet = videoRoot.Items?.FirstOrDefault()?.Snippet;
                if (snippet != null)
                {
                    livestreamModel.DisplayName = snippet.ChannelTitle;
                    livestreamModel.Description = snippet.Title;
                    livestreamModel.ThumbnailUrls = new ThumbnailUrls()
                    {
                        Small = snippet.Thumbnails?.Standard?.Url,
                        Large = snippet.Thumbnails?.High?.Url,
                        Medium = snippet.Thumbnails?.Medium?.Url
                    };

                    var livestreamDetails = videoRoot.Items?.FirstOrDefault()?.LiveStreamingDetails;
                    if (livestreamDetails != null)
                    {
                        livestreamModel.Viewers = livestreamDetails.ConcurrentViewers;

                        if (livestreamDetails.ActualStartTime.HasValue)
                        {
                            livestreamModel.StartTime = livestreamDetails.ActualStartTime.Value;
                            livestreamModel.Live = snippet.LiveBroadcastContent != "none";
                            isOffline = livestreamModel.Live;
                        }
                    }
                }

                if (isOffline)
                    offlineStreams.Add(livestreamModel);
            }

            return offlineStreams;
        }

        public Task UpdateOfflineStreams(List<LivestreamModel> livestreams, CancellationToken cancellationToken)
        {
            livestreams.ForEach(x => x.Offline());
            return Task.CompletedTask;
        }

        public Task<List<VodDetails>> GetVods(VodQuery vodQuery)
        {
            return Task.FromResult(new List<VodDetails>());
        }
    }
}