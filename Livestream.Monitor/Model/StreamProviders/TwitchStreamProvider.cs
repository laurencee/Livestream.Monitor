using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Livestream.Monitor.Core;
using Livestream.Monitor.Model.Monitoring;
using TwitchTv;

namespace Livestream.Monitor.Model.StreamProviders
{
    public class TwitchStreamProvider : IStreamProvider
    {
        public const string PROVIDER_NAME = "twitchtv";

        private readonly ITwitchTvReadonlyClient twitchTvClient;

        public TwitchStreamProvider(ITwitchTvReadonlyClient twitchTvClient)
        {
            if (twitchTvClient == null) throw new ArgumentNullException(nameof(twitchTvClient));
            this.twitchTvClient = twitchTvClient;
        }

        public string ProviderName => PROVIDER_NAME;

        public string BaseUrl => @"http://www.twitch.tv/";

        public bool HasChatSupport => true;

        public bool HasVodViewerSupport => true;

        public string GetStreamUrl(string streamId)
        {
            if (string.IsNullOrWhiteSpace(streamId)) throw new ArgumentNullException(nameof(streamId));

            return $"{BaseUrl}{streamId}/";
        }

        public string GetChatUrl(string streamId)
        {
            if (string.IsNullOrWhiteSpace(streamId)) throw new ArgumentNullException(nameof(streamId));

            return $"{BaseUrl}{streamId}/chat?popout=true";
        }

        public async Task<List<LivestreamModel>> UpdateOnlineStreams(List<LivestreamModel> livestreams)
        {
            var onlineStreams = await twitchTvClient.GetStreamsDetails(livestreams.Select(x => x.Id).ToList());

            foreach (var onlineStream in onlineStreams)
            {
                var livestream = livestreams.Single(x => x.Id.IsEqualTo(onlineStream.Channel.Name));

                livestream.PopulateWithChannel(onlineStream.Channel);
                livestream.PopulateWithStreamDetails(onlineStream, this);
            }
            
            return livestreams.Where(x => !onlineStreams.Any(y => y.Channel.Name.IsEqualTo(x.Id))).ToList();
        }

        public async Task UpdateOfflineStreams(List<LivestreamModel> livestreams)
        {
            var offlineTasks = livestreams.Select(x => new
            {
                Livestream = x,
                OfflineData = twitchTvClient.GetChannelDetails(x.Id)
            }).ToList();

            await Task.WhenAll(offlineTasks.Select(x => x.OfflineData));
            foreach (var offlineTask in offlineTasks)
            {
                var offlineData = offlineTask.OfflineData.Result;
                if (offlineData == null) continue;

                offlineTask.Livestream.PopulateWithChannel(offlineData);
            }
        }
    }
}